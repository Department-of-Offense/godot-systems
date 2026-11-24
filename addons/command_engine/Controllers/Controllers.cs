using CommandEngine.Core;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.Controllers;

/// <summary>Anything that can drive a unit: AI orders, a local pilot, later a network peer.</summary>
public interface IUnitController
{
    UnitIntent ComputeIntent(Unit unit, double delta);
}

/// <summary>
/// The AI driver. Works through the unit's command queue and, when idle,
/// returns fire at enemies that come into weapon range without moving.
/// </summary>
public sealed class CommandController : IUnitController
{
    public bool AutoEngageWhenIdle { get; set; } = true;

    public UnitIntent ComputeIntent(Unit unit, double delta)
    {
        var intent = new UnitIntent();
        unit.Commands.Tick(unit, delta, ref intent);

        if (unit.Commands.Current is null && AutoEngageWhenIdle && unit.Weapon is not null)
        {
            var enemy = UnitRegistry.FindNearestEnemy(unit, unit.Weapon.Range * 0.85f);
            if (enemy is not null)
            {
                var to = UnitMath.Flat(enemy.GlobalPosition - unit.GlobalPosition);
                if (to.LengthSquared() > 0.0001f) intent.FaceYaw = UnitMath.YawFor(to);
                intent.AimAt = enemy.AimPoint;
                intent.Fire = unit.IsFacing(enemy.GlobalPosition, 0.35f);
            }
        }
        return intent;
    }
}

/// <summary>
/// The human driver. Turns mouse and keyboard input into the same
/// <see cref="UnitIntent"/> the AI produces. The camera rigs read
/// <see cref="LookYaw"/> and <see cref="LookPitch"/> to place the camera.
/// </summary>
public sealed class PilotController : IUnitController
{
    public float MouseSensitivity { get; set; } = 0.0025f;
    public float MinPitch { get; set; } = -1.35f;
    public float MaxPitch { get; set; } = 1.35f;

    public float LookYaw { get; set; }
    public float LookPitch { get; set; }

    /// <summary>
    /// True for first-person and chase views: the body turns with the mouse.
    /// False for the orbit view: the body turns toward where it walks.
    /// </summary>
    public bool BodyFollowsLook { get; set; } = true;

    /// <summary>Camera used to resolve the crosshair into a world aim point.</summary>
    public Camera3D? Camera { get; set; }

    public float AimDistance { get; set; } = 500f;

    internal void Attach(Unit unit)
    {
        LookYaw = unit.Rotation.Y;
        LookPitch = unit.AimPitch;
    }

    public void HandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            LookYaw -= motion.Relative.X * MouseSensitivity;
            LookPitch = Mathf.Clamp(LookPitch - motion.Relative.Y * MouseSensitivity, MinPitch, MaxPitch);
        }
    }

    public UnitIntent ComputeIntent(Unit unit, double delta)
    {
        var input = Input.GetVector(
            InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);
        var move = new Basis(Vector3.Up, LookYaw) * new Vector3(input.X, 0f, input.Y);
        var fire = Input.IsActionPressed(InputActions.Fire);

        var intent = new UnitIntent
        {
            Move = move.LimitLength(1f),
            Jump = Input.IsActionJustPressed(InputActions.Jump),
            Fire = fire,
            AimAt = ResolveAimPoint(unit),
        };

        if (BodyFollowsLook || fire)
        {
            intent.FaceYaw = LookYaw;
            intent.SnapFacing = true;
            intent.AimPitch = LookPitch;
        }
        else if (move.LengthSquared() > 0.01f)
        {
            intent.FaceYaw = UnitMath.YawFor(move);
        }
        return intent;
    }

    /// <summary>
    /// Ray from the camera through screen centre. Shots go from the unit's
    /// muzzle to whatever is under the crosshair, so third-person aiming
    /// lands where the player is looking rather than where the gun points.
    /// </summary>
    private Vector3? ResolveAimPoint(Unit unit)
    {
        if (Camera is null || !Camera.IsInsideTree()) return null;

        var origin = Camera.GlobalPosition;
        var direction = -Camera.GlobalBasis.Z;
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * AimDistance);
        query.Exclude = new Godot.Collections.Array<Rid> { unit.GetRid() };
        var hit = unit.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 ? (Vector3)hit["position"] : origin + direction * AimDistance;
    }
}
