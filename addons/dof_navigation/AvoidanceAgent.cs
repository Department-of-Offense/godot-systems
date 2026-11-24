using System;
using Godot;

namespace Dof.Navigation;

/// <summary>
/// Local avoidance (RVO) on Godot's navigation server, for any mover. Each
/// tick, tell it where the agent is and the velocity it wants; it hands back
/// a velocity that steers around nearby agents. Paths say where to go;
/// avoidance stops a crowd following the same path from piling into a clump.
///
/// The safe velocity is computed during the server's sync, so it lags the
/// request by one physics tick. That's invisible at unit speeds.
///
/// Owns a server resource: call <see cref="Attach"/> when the mover enters a
/// world and <see cref="Detach"/> when it leaves or dies.
/// </summary>
public sealed class AvoidanceAgent
{
    // Radius, MaxSpeed and the neighbour settings are sent on Attach; call
    // ApplySettings() after changing them on an attached agent.

    /// <summary>Personal-space radius in metres. A little larger than the collider works well.</summary>
    public float Radius { get; set; } = 0.5f;

    /// <summary>Fastest the agent can move (m/s); safe velocities never exceed it.</summary>
    public float MaxSpeed { get; set; } = 6f;

    /// <summary>Only agents within this distance (metres) are considered.</summary>
    public float NeighborDistance { get; set; } = 5f;

    public int MaxNeighbors { get; set; } = 8;

    /// <summary>How far ahead (seconds) to look for collisions with other agents. Higher = earlier, smoother swerves.</summary>
    public float TimeHorizon { get; set; } = 1f;

    /// <summary>
    /// 0..1. An agent only gives way to agents of equal or higher priority,
    /// so a high-priority agent (e.g. a player-piloted unit) is avoided by
    /// others without being pushed around itself.
    /// </summary>
    public float Priority { get; set; } = 0.5f;

    /// <summary>
    /// A standing agent steps aside for higher-priority movers only when the
    /// solver asks for at least this speed (m/s). Set to
    /// <see cref="float.PositiveInfinity"/> to pin standing agents in place.
    /// </summary>
    public float YieldSpeed { get; set; } = 0.5f;

    public bool IsAttached => _rid.IsValid;

    private Rid _rid;
    private Vector3 _safeVelocity;
    private bool _hasSafeVelocity;
    private float _appliedPriority = float.NaN;

    public void Attach(World3D world)
    {
        if (_rid.IsValid) Detach();
        _rid = NavigationServer3D.AgentCreate();
        NavigationServer3D.AgentSetMap(_rid, world.NavigationMap);
        NavigationServer3D.AgentSetAvoidanceCallback(_rid, Callable.From<Vector3>(OnSafeVelocity));
        NavigationServer3D.AgentSetAvoidanceEnabled(_rid, true);
        ApplySettings();
    }

    public void Detach()
    {
        if (!_rid.IsValid) return;
        NavigationServer3D.FreeRid(_rid);
        _rid = default;
        _hasSafeVelocity = false;
    }

    /// <summary>
    /// Report this tick's position and desired velocity. With
    /// <paramref name="obey"/> it returns the avoidance velocity to move at;
    /// without, it returns <paramref name="desired"/> unchanged but others
    /// still avoid this agent (use that for a player-controlled mover).
    /// </summary>
    public Vector3 Steer(Vector3 position, Vector3 desired, bool obey = true)
    {
        if (!_rid.IsValid) return desired;
        if (_appliedPriority != Priority) ApplySettings();
        NavigationServer3D.AgentSetPosition(_rid, position);
        NavigationServer3D.AgentSetVelocity(_rid, desired);

        if (!obey || !_hasSafeVelocity) return desired;

        // An agent that wants to stand still only moves to make way for
        // higher-priority movers, and ignores tiny solver jitter so it
        // doesn't drift off its spot.
        if (desired.LengthSquared() < 0.0001f)
            return _safeVelocity.LengthSquared() >= YieldSpeed * YieldSpeed ? _safeVelocity : Vector3.Zero;
        return _safeVelocity;
    }

    /// <summary>Push changed Radius, MaxSpeed and neighbour settings to the server (Priority is pushed automatically).</summary>
    public void ApplySettings()
    {
        if (!_rid.IsValid) return;
        _appliedPriority = Priority;
        NavigationServer3D.AgentSetRadius(_rid, Radius);
        NavigationServer3D.AgentSetMaxSpeed(_rid, MaxSpeed);
        NavigationServer3D.AgentSetNeighborDistance(_rid, NeighborDistance);
        NavigationServer3D.AgentSetMaxNeighbors(_rid, MaxNeighbors);
        NavigationServer3D.AgentSetTimeHorizonAgents(_rid, TimeHorizon);
        NavigationServer3D.AgentSetAvoidancePriority(_rid, Math.Clamp(Priority, 0f, 1f));
    }

    private void OnSafeVelocity(Vector3 velocity)
    {
        _safeVelocity = velocity;
        _hasSafeVelocity = true;
    }
}
