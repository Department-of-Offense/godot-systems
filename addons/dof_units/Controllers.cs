using Dof.Core;
using Godot;

namespace Dof.Units;

/// <summary>
/// Anything that can drive a unit: AI orders, a local pilot, a network peer,
/// a scripted cutscene. Implement this to add a new kind of control.
/// </summary>
public interface IUnitController
{
    UnitIntent ComputeIntent(Unit unit, double delta);
    void OnAttached(Unit unit) { }
    void OnDetached(Unit unit) { }
}

/// <summary>
/// Default AI: works through the unit's order queue and, when idle and
/// <see cref="Unit.AutoEngageWhenIdle"/> is on, returns fire at enemies in
/// range without moving.
/// </summary>
public class OrderController : IUnitController
{
    public virtual UnitIntent ComputeIntent(Unit unit, double delta)
    {
        var intent = new UnitIntent();
        unit.Commands.Tick(unit, delta, ref intent);

        if (unit.Commands.Current is null && unit.AutoEngageWhenIdle && unit.Weapon is not null
            && unit.NearestEnemyInRange() is { } enemy)
        {
            UnitCommand.Engage(unit, enemy, ref intent);
        }
        return intent;
    }
}

/// <summary>
/// Local human driver: turns mouse and keyboard into the same
/// <see cref="UnitIntent"/> the AI produces. Cameras read
/// <see cref="LookYaw"/> / <see cref="LookPitch"/> to place themselves.
/// </summary>
public class PilotController : IUnitController
{
    public float MouseSensitivity { get; set; } = 0.0025f;
    public float MinPitch { get; set; } = -1.35f;
    public float MaxPitch { get; set; } = 1.35f;
    public float AimDistance { get; set; } = 500f;

    public string MoveForwardAction { get; set; } = CoreActions.MoveForward;
    public string MoveBackAction { get; set; } = CoreActions.MoveBack;
    public string MoveLeftAction { get; set; } = CoreActions.MoveLeft;
    public string MoveRightAction { get; set; } = CoreActions.MoveRight;
    public string JumpAction { get; set; } = UnitActions.Jump;
    public string FireAction { get; set; } = UnitActions.Fire;

    public float LookYaw { get; set; }
    public float LookPitch { get; set; }

    /// <summary>
    /// True: the body turns with the mouse (first person, chase).
    /// False: the body turns toward where it walks (orbit), and to the aim when firing.
    /// </summary>
    public bool BodyFollowsLook { get; set; } = true;

    /// <summary>Camera used to turn the crosshair into a world aim point. Optional.</summary>
    public Camera3D? Camera { get; set; }

    public PilotController() => UnitActions.EnsureDefaults();

    public virtual void OnAttached(Unit unit)
    {
        LookYaw = unit.Rotation.Y;
        LookPitch = unit.AimPitch;
    }

    /// <summary>Feed mouse motion here (only while the mouse is captured).</summary>
    public virtual void HandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            LookYaw -= motion.Relative.X * MouseSensitivity;
            LookPitch = Mathf.Clamp(LookPitch - motion.Relative.Y * MouseSensitivity, MinPitch, MaxPitch);
        }
    }

    public virtual UnitIntent ComputeIntent(Unit unit, double delta)
    {
        var input = Input.GetVector(MoveLeftAction, MoveRightAction, MoveForwardAction, MoveBackAction);
        var move = new Basis(Vector3.Up, LookYaw) * new Vector3(input.X, 0f, input.Y);
        var fire = Input.IsActionPressed(FireAction);

        var intent = new UnitIntent
        {
            Move = move.LimitLength(1f),
            Jump = Input.IsActionJustPressed(JumpAction),
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
            intent.FaceYaw = Math3D.YawFor(move);
        }
        return intent;
    }

    /// <summary>
    /// Ray from the camera through screen centre. Shots go from the unit's
    /// eye to whatever is under the crosshair, so third-person aiming lands
    /// where the player is looking.
    /// </summary>
    protected Vector3? ResolveAimPoint(Unit unit)
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

public static class UnitActions
{
    public const string Jump = "dof_jump";
    public const string Fire = "dof_fire";

    public static void EnsureDefaults()
    {
        CoreActions.EnsureDefaults();
        InputDefaults.Keys(Jump, Key.Space);
        InputDefaults.Mouse(Fire, MouseButton.Left);
    }
}
