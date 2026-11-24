using Godot;

namespace CommandEngine.Units;

/// <summary>
/// Hitscan weapon. The same weapon fires whether the unit is under AI orders
/// or piloted by a player; only where the aim comes from differs.
/// </summary>
[GlobalClass]
public partial class Weapon : Node3D
{
    [Signal] public delegate void FiredEventHandler(Vector3 from, Vector3 to, Node? hit);

    [Export] public float Damage { get; set; } = 10f;
    [Export] public float FireInterval { get; set; } = 0.2f;
    [Export] public float Range { get; set; } = 40f;
    [Export] public bool FriendlyFire { get; set; }
    [Export] public bool ShowTracers { get; set; } = true;
    [Export] public Color TracerColor { get; set; } = new(1f, 0.85f, 0.4f);

    private double _cooldown;

    public bool IsReady => _cooldown <= 0;

    public override void _PhysicsProcess(double delta)
    {
        if (_cooldown > 0) _cooldown -= delta;
    }

    /// <summary>Fire from <paramref name="origin"/> along <paramref name="direction"/> if off cooldown.</summary>
    /// <returns>True if a shot was fired.</returns>
    public bool TryFire(Unit shooter, Vector3 origin, Vector3 direction)
    {
        if (!IsReady || direction.LengthSquared() < 0.0001f) return false;
        _cooldown = FireInterval;

        var end = origin + direction.Normalized() * Range;
        var query = PhysicsRayQueryParameters3D.Create(origin, end);
        query.Exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        Node? hitNode = null;
        if (hit.Count > 0)
        {
            end = (Vector3)hit["position"];
            hitNode = hit["collider"].AsGodotObject() as Node;
            var victim = UnitRegistry.FromCollider(hitNode);
            if (victim is not null && (FriendlyFire || victim.Team != shooter.Team))
                victim.Health.TakeDamage(Damage, shooter);
        }

        if (ShowTracers) SpawnTracer(origin, end);
        EmitSignal(SignalName.Fired, origin, end, hitNode!);
        return true;
    }

    private void SpawnTracer(Vector3 from, Vector3 to)
    {
        var length = from.DistanceTo(to);
        if (length < 0.05f) return;

        var tracer = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.04f, length) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = TracerColor,
            },
        };
        // Tracers live in the world, not on the unit, so they don't swing with it.
        GetTree().CurrentScene?.AddChild(tracer);
        if (!tracer.IsInsideTree()) { tracer.QueueFree(); return; }
        var midpoint = (from + to) * 0.5f;
        tracer.LookAtFromPosition(midpoint, to, Mathf.Abs((to - from).Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up);
        GetTree().CreateTimer(0.05).Timeout += tracer.QueueFree;
    }
}
