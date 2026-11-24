using System;
using Dof.Core;
using Godot;

namespace Dof.Navigation;

/// <summary>
/// Turns "go to X" into "steer toward this next corner" using the world's
/// navigation map. Works for any Node3D, not just units. One per agent.
///
/// When the world has no navmesh it falls back to the target itself, so
/// agents still walk straight lines in scenes that never baked one.
/// Re-planning is throttled and spread out across agents, so hundreds of
/// agents don't all query paths on the same frame.
/// </summary>
public sealed class PathFollower
{
    /// <summary>Seconds between re-plans while the target stays put.</summary>
    public float RepathInterval { get; set; } = 0.75f;

    /// <summary>Re-plan straight away if the target moves further than this (metres).</summary>
    public float TargetMoveTolerance { get; set; } = 1.0f;

    /// <summary>Advance to the next corner once this close to the current one (metres, horizontal).</summary>
    public float CornerReachedDistance { get; set; } = 0.6f;

    /// <summary>Navigation layers this agent may walk on.</summary>
    public uint NavigationLayers { get; set; } = 1;

    /// <summary>The corners of the current path (empty if walking straight).</summary>
    public ReadOnlySpan<Vector3> Corners => _path;

    /// <summary>Index into <see cref="Corners"/> of the corner being steered to.</summary>
    public int CornerIndex { get; private set; }

    /// <summary>
    /// Where the agent will actually end up: the requested target, or the
    /// nearest reachable point to it if the target is inside an obstacle or
    /// on another island.
    /// </summary>
    public Vector3 Goal { get; private set; }

    /// <summary>
    /// Seconds since the agent last got meaningfully closer to <see cref="Goal"/>.
    /// Resets when the target moves. Use it to give up or settle for "close
    /// enough" when something (often other agents) is in the way.
    /// </summary>
    public float StuckSeconds { get; private set; }

    /// <summary>Progress smaller than this (metres) doesn't count as getting closer.</summary>
    public float ProgressThreshold { get; set; } = 0.25f;

    /// <summary>True if the last plan used the navmesh (false = straight-line fallback).</summary>
    public bool UsingNavmesh { get; private set; }

    private Vector3[] _path = Array.Empty<Vector3>();
    private Vector3 _requested;
    private bool _hasPlan;
    private float _bestDistance = float.MaxValue;
    private ulong _nextRepathFrame;
    private readonly ulong _phase = (ulong)Random.Shared.Next(0, 1 << 16);

    /// <summary>
    /// Point to steer toward this tick to make progress to <paramref name="target"/>.
    /// Call from the physics step.
    /// </summary>
    public Vector3 NextPoint(Node3D agent, Vector3 target)
    {
        var frame = Engine.GetPhysicsFrames();
        var targetMoved = !_hasPlan || Math3D.Flat(target - _requested).LengthSquared() > TargetMoveTolerance * TargetMoveTolerance;
        if (targetMoved)
        {
            _bestDistance = float.MaxValue;
            StuckSeconds = 0f;
        }
        if (targetMoved || frame >= _nextRepathFrame)
            Plan(agent, target, frame);

        var position = agent.GlobalPosition;
        var distance = Math3D.Flat(Goal - position).Length();
        if (distance < _bestDistance - ProgressThreshold)
        {
            _bestDistance = distance;
            StuckSeconds = 0f;
        }
        else
        {
            StuckSeconds += 1f / Engine.PhysicsTicksPerSecond;
        }

        if (!UsingNavmesh || _path.Length == 0) return target;

        while (CornerIndex < _path.Length - 1
               && Math3D.Flat(_path[CornerIndex] - position).Length() <= CornerReachedDistance)
            CornerIndex++;
        return _path[CornerIndex];
    }

    /// <summary>Forget the current plan (e.g. after a teleport).</summary>
    public void Reset()
    {
        _hasPlan = false;
        _path = Array.Empty<Vector3>();
        CornerIndex = 0;
        _bestDistance = float.MaxValue;
        StuckSeconds = 0f;
    }

    private void Plan(Node3D agent, Vector3 target, ulong frame)
    {
        _requested = target;
        _hasPlan = true;
        var ticks = (ulong)Math.Max(1, Engine.PhysicsTicksPerSecond * RepathInterval);
        // Spread agents across the interval so they don't all re-plan on one frame.
        _nextRepathFrame = frame + ticks - (frame + _phase) % ticks;

        var map = agent.GetWorld3D().NavigationMap;
        var path = NavigationQueries.HasNavmesh(map)
            ? NavigationServer3D.MapGetPath(map, agent.GlobalPosition, target, true, NavigationLayers)
            : Array.Empty<Vector3>();

        if (path.Length == 0)
        {
            UsingNavmesh = false;
            _path = Array.Empty<Vector3>();
            Goal = target;
            CornerIndex = 0;
            return;
        }

        UsingNavmesh = true;
        _path = path;
        Goal = path[^1];
        // path[0] is (roughly) where we stand; steer to the first real corner.
        CornerIndex = path.Length > 1 ? 1 : 0;
    }
}

public static class NavigationQueries
{
    /// <summary>True once the map has at least one region and has synced, so path queries mean something.</summary>
    public static bool HasNavmesh(Rid map) =>
        map.IsValid && NavigationServer3D.MapGetIterationId(map) > 0 && NavigationServer3D.MapGetRegions(map).Count > 0;

    /// <summary>Nearest point on the navmesh to <paramref name="point"/>, or the point itself if there's no navmesh.</summary>
    public static Vector3 ClosestWalkable(World3D world, Vector3 point) =>
        HasNavmesh(world.NavigationMap) ? NavigationServer3D.MapGetClosestPoint(world.NavigationMap, point) : point;
}
