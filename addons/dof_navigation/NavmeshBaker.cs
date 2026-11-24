using Godot;

namespace Dof.Navigation;

/// <summary>
/// A NavigationRegion3D that bakes its navmesh at runtime from the static
/// collision under <see cref="SourceRoot"/>. Use it for levels built in code
/// or changed at runtime. For hand-authored levels you can instead bake a
/// NavigationRegion3D in the editor; agents use either.
///
/// Only StaticBody3D colliders are parsed, so units (CharacterBody3D) and
/// other moving bodies never carve holes in the mesh.
/// </summary>
[GlobalClass]
public partial class NavmeshBaker : NavigationRegion3D
{
    /// <summary>Emitted once the new navmesh is live on the navigation map and paths can be queried.</summary>
    [Signal] public delegate void BakedEventHandler();

    /// <summary>Node whose descendants are scanned for static colliders. Defaults to this node's parent.</summary>
    [Export] public Node? SourceRoot { get; set; }

    [Export] public bool BakeOnReady { get; set; } = true;

    /// <summary>Bake on a worker thread. The scene keeps running; agents walk straight until it finishes.</summary>
    [Export] public bool BakeAsync { get; set; }

    /// <summary>
    /// Voxel size in metres. Bake cost grows with the square of 1/CellSize.
    /// Recast simplifies outlines by up to ~1.3 cells, which can cut into the
    /// clearance around corners: keep CellSize * 1.3 below
    /// (AgentRadius - collider radius) + AgentRadius, or agents snag on
    /// corners. Applied to the navigation map too, since regions on one map
    /// must share a cell size.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,2,0.05")] public float CellSize { get; set; } = 0.25f;

    [ExportGroup("Agent")]
    [Export] public float AgentRadius { get; set; } = 0.5f;
    [Export] public float AgentHeight { get; set; } = 1.8f;
    [Export] public float AgentMaxClimb { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0,89,1")] public float AgentMaxSlopeDegrees { get; set; } = 45f;

    [ExportGroup("Source")]
    [Export(PropertyHint.Layers3DPhysics)] public uint SourceCollisionMask { get; set; } = uint.MaxValue;

    public bool BakeInProgress { get; private set; }

    public override void _Ready()
    {
        // Wait one frame so siblings added after us in the same _Ready are parsed too.
        if (BakeOnReady) Callable.From(() => Bake(BakeAsync)).CallDeferred();
    }

    /// <summary>(Re)bake now. Call again after moving or adding static geometry.</summary>
    public void Bake(bool async = false)
    {
        if (BakeInProgress) return;
        var root = SourceRoot ?? GetParent();
        if (root is null) return;

        var map = GetWorld3D().NavigationMap;
        var cellSize = CellSize;
        NavigationServer3D.MapSetCellSize(map, cellSize);
        var cellHeight = NavigationServer3D.MapGetCellHeight(map);
        var mesh = new NavigationMesh
        {
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.RootNodeChildren,
            GeometryCollisionMask = SourceCollisionMask,
            // Recast works in whole voxels: snap agent sizes to the cell grid
            // (radius and height round up to stay safe, climb rounds down).
            AgentRadius = Mathf.Ceil(AgentRadius / cellSize) * cellSize,
            AgentHeight = Mathf.Ceil(AgentHeight / cellHeight) * cellHeight,
            AgentMaxClimb = Mathf.Floor(AgentMaxClimb / cellHeight) * cellHeight,
            AgentMaxSlope = AgentMaxSlopeDegrees,
            CellSize = cellSize,
            CellHeight = cellHeight,
        };

        var source = new NavigationMeshSourceGeometryData3D();
        NavigationServer3D.ParseSourceGeometryData(mesh, source, root);

        BakeInProgress = true;
        if (async)
        {
            NavigationServer3D.BakeFromSourceGeometryDataAsync(mesh, source, Callable.From(() => Finish(mesh)));
        }
        else
        {
            NavigationServer3D.BakeFromSourceGeometryData(mesh, source);
            Finish(mesh);
        }
    }

    private void Finish(NavigationMesh mesh)
    {
        // The async callback may arrive off the main thread; apply on the main thread.
        Callable.From(() =>
        {
            if (!IsInstanceValid(this)) return; // freed while an async bake was running
            NavigationMesh = mesh;
            // The navigation map syncs on its own (possibly threaded) schedule.
            // Only report Baked once a sync has picked up the new mesh, so
            // listeners can query paths straight away.
            _awaitingSync = true;
        }).CallDeferred();
    }

    private bool _awaitingSync;

    // A MapChanged can belong to a sync that started before the mesh arrived;
    // check the map really serves our polygons before announcing Baked.
    private bool MapContainsThisRegion()
    {
        var vertices = NavigationMesh?.GetVertices();
        if (vertices is null || vertices.Length == 0) return true; // nothing walkable to wait for
        var probe = GlobalTransform * vertices[0];
        return NavigationServer3D.MapGetClosestPointOwner(GetNavigationMap(), probe) == GetRid();
    }

    public override void _EnterTree() => NavigationServer3D.MapChanged += OnMapChanged;

    public override void _ExitTree() => NavigationServer3D.MapChanged -= OnMapChanged;

    private void OnMapChanged(Rid map)
    {
        if (!_awaitingSync || map != GetNavigationMap() || !MapContainsThisRegion()) return;
        _awaitingSync = false;
        BakeInProgress = false;
        EmitSignal(SignalName.Baked);
    }
}
