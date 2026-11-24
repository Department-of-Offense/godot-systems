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
    public string AimAction { get; set; } = UnitActions.Aim;
    public string ReloadAction { get; set; } = UnitActions.Reload;

    /// <summary>Mouse sensitivity is divided by the weapon's zoom while aiming, so aim feels the same zoomed in.</summary>
    public bool ScaleSensitivityWhenAiming { get; set; } = true;

    /// <summary>How fast the view drifts back down after recoil (per second; higher = snappier).</summary>
    public float RecoilRecovery { get; set; } = 5f;

    /// <summary>Recoil multiplier while aiming down sights.</summary>
    public float AimingRecoilMultiplier { get; set; } = 0.7f;

    /// <summary>True while the aim button is held (updated each tick).</summary>
    public bool IsAiming { get; private set; }

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

    private Unit? _unit;
    private float _recoilDebt;

    public virtual void OnAttached(Unit unit)
    {
        LookYaw = unit.Rotation.Y;
        LookPitch = unit.AimPitch;
        _unit = unit;
        _recoilDebt = 0f;
        if (unit.Weapon is not null) unit.Weapon.Recoiled += OnRecoil;
    }

    public virtual void OnDetached(Unit unit)
    {
        if (unit.Weapon is not null) unit.Weapon.Recoiled -= OnRecoil;
        _unit = null;
        IsAiming = false;
    }

    /// <summary>Kick the view. Pitch recoil is recovered over time; sideways kick stays (the player corrects it).</summary>
    protected virtual void OnRecoil(Vector2 kick)
    {
        var scale = IsAiming ? AimingRecoilMultiplier : 1f;
        LookPitch = Mathf.Clamp(LookPitch + kick.X * scale, MinPitch, MaxPitch);
        LookYaw += kick.Y * scale;
        _recoilDebt += kick.X * scale;
    }

    /// <summary>Feed mouse motion here (only while the mouse is captured).</summary>
    public virtual void HandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            var sensitivity = MouseSensitivity;
            if (IsAiming && ScaleSensitivityWhenAiming && _unit?.Weapon is { } weapon) sensitivity /= Mathf.Max(1f, weapon.AdsZoom);
            LookYaw -= motion.Relative.X * sensitivity;
            LookPitch = Mathf.Clamp(LookPitch - motion.Relative.Y * sensitivity, MinPitch, MaxPitch);
        }
    }

    public virtual UnitIntent ComputeIntent(Unit unit, double delta)
    {
        var input = Input.GetVector(MoveLeftAction, MoveRightAction, MoveForwardAction, MoveBackAction);
        var move = new Basis(Vector3.Up, LookYaw) * new Vector3(input.X, 0f, input.Y);
        var fire = Input.IsActionPressed(FireAction);
        IsAiming = Input.IsActionPressed(AimAction) && unit.Weapon is not null;

        if (_recoilDebt > 0f)
        {
            var recover = _recoilDebt * (1f - Mathf.Exp(-RecoilRecovery * (float)delta));
            _recoilDebt -= recover;
            LookPitch = Mathf.Clamp(LookPitch - recover, MinPitch, MaxPitch);
        }

        var intent = new UnitIntent
        {
            Move = move.LimitLength(1f),
            Jump = Input.IsActionJustPressed(JumpAction),
            Fire = fire,
            AimDownSights = IsAiming,
            Reload = Input.IsActionJustPressed(ReloadAction),
            AimAt = ResolveAimPoint(unit),
        };

        if (BodyFollowsLook || fire || IsAiming)
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
    public const string Aim = "dof_aim";
    public const string Reload = "dof_reload";

    public static void EnsureDefaults()
    {
        CoreActions.EnsureDefaults();
        InputDefaults.Keys(Jump, Key.Space);
        InputDefaults.Mouse(Fire, MouseButton.Left);
        InputDefaults.Mouse(Aim, MouseButton.Right);
        InputDefaults.Keys(Reload, Key.R);
    }
}
