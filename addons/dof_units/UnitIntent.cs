using Godot;

namespace Dof.Units;

/// <summary>
/// What a unit wants to do this physics tick. Every controller (AI command
/// execution, a human pilot, a network peer later) produces one of these, and
/// <see cref="Unit"/> applies it the same way. That is what makes "possess any
/// unit" cheap: the unit never knows who is driving it.
/// </summary>
public struct UnitIntent
{
    /// <summary>World-space horizontal move direction, length 0..1.</summary>
    public Vector3 Move;

    /// <summary>Desired body yaw in radians (Godot convention: 0 faces -Z).</summary>
    public float? FaceYaw;

    /// <summary>Snap to <see cref="FaceYaw"/> instead of turning smoothly (pilot mouse look).</summary>
    public bool SnapFacing;

    /// <summary>Desired aim pitch in radians, positive looks up.</summary>
    public float? AimPitch;

    /// <summary>World point to aim the weapon at. Overrides yaw/pitch aiming when set.</summary>
    public Vector3? AimAt;

    public bool Fire;
    public bool Jump;
}
