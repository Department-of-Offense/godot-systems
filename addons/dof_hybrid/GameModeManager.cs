using Dof.Cameras;
using Dof.Command;
using Dof.Core;
using Dof.Units;
using Godot;

namespace Dof.Hybrid;

/// <summary>
/// The strategy ↔ pilot loop. In <see cref="Mode.Strategy"/> the player
/// commands units from above; the pilot key on a selected unit switches to
/// <see cref="Mode.Pilot"/> and the player drives it through a follow camera.
/// Every other unit keeps carrying out its orders.
///
/// This is glue over the cameras, command and units systems. Assign your own
/// (or subclassed) <see cref="Director"/> and <see cref="Selection"/>, or leave
/// them empty and defaults are created. Override <see cref="CanPilot"/> and
/// <see cref="CreatePilot"/> to change the rules.
/// </summary>
[GlobalClass]
public partial class GameModeManager : Node
{
    public enum Mode { Strategy, Pilot }

    public const string Group = "dof_game_mode_manager";

    [Signal] public delegate void ModeChangedEventHandler(Mode mode, Unit? pilotedUnit);

    [Export] public int PlayerTeam { get; set; }
    [Export] public CameraDirector? Director { get; set; }
    [Export] public SelectionManager? Selection { get; set; }

    /// <summary>Follow view to start in (index into the director's FollowViews).</summary>
    [Export] public int DefaultPilotView { get; set; } = 1;

    [Export] public string PilotToggleAction { get; set; } = HybridActions.PilotToggle;
    [Export] public string CancelAction { get; set; } = HybridActions.Cancel;
    [Export] public string CycleViewAction { get; set; } = HybridActions.CycleView;

    public Mode CurrentMode { get; private set; } = Mode.Strategy;
    public Unit? PilotedUnit { get; private set; }
    public PilotController? Pilot { get; private set; }

    private int? _pilotView;

    public override void _Ready()
    {
        HybridActions.EnsureDefaults();
        AddToGroup(Group);

        if (Director is null)
        {
            Director = new CameraDirector { Name = "CameraDirector" };
            AddChild(Director);
        }

        if (Selection is null)
        {
            var overlay = new CanvasLayer { Name = "SelectionOverlay" };
            AddChild(overlay);
            Selection = new SelectionManager { Name = "Selection", PlayerTeam = PlayerTeam };
            overlay.AddChild(Selection);
        }
        Selection.Camera ??= Director.Camera;

        Callable.From(FocusOnPlayerForces).CallDeferred();
    }

    /// <summary>Zoom the follow view while the pilot aims down sights. Override <see cref="AimZoom"/> to change it.</summary>
    public override void _Process(double delta)
    {
        if (Director?.Active is FollowRig rig)
            rig.Zoom = CurrentMode == Mode.Pilot && Pilot is { IsAiming: true } && PilotedUnit is { Weapon: { } weapon }
                ? AimZoom(weapon.AdsZoom)
                : 1f;
    }

    /// <summary>Camera zoom to use while aiming with a weapon whose <c>AdsZoom</c> is <paramref name="weaponZoom"/>.</summary>
    protected virtual float AimZoom(float weaponZoom) => weaponZoom;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (CurrentMode == Mode.Strategy)
        {
            if (@event.IsActionPressed(PilotToggleAction) && Selection?.Primary is { } unit && TryPilot(unit))
                GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(PilotToggleAction) || @event.IsActionPressed(CancelAction))
        {
            ReturnToStrategy();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(CycleViewAction))
        {
            CyclePilotView();
            GetViewport().SetInputAsHandled();
        }
        else
        {
            Pilot?.HandleInput(@event);
        }
    }

    /// <summary>Whether the player may take control of <paramref name="unit"/>.</summary>
    public virtual bool CanPilot(Unit unit) => IsInstanceValid(unit) && unit.IsAlive && unit.Team == PlayerTeam;

    /// <summary>Controller used while piloting. Override per unit type (vehicles, aircraft...).</summary>
    protected virtual PilotController CreatePilot(Unit unit) => new();

    public bool TryPilot(Unit unit)
    {
        if (!CanPilot(unit) || Director is null) return false;
        if (PilotedUnit == unit) return true;
        if (PilotedUnit is not null) ReleasePilotedUnit();

        Pilot = CreatePilot(unit);
        Pilot.Camera = Director.Camera;
        PilotedUnit = unit;
        unit.BeginPilot(Pilot);
        unit.Died += OnPilotedUnitDied;

        if (Selection is not null) Selection.Enabled = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        ShowView(_pilotView ?? DefaultPilotView);

        CurrentMode = Mode.Pilot;
        EmitSignal(SignalName.ModeChanged, (int)CurrentMode, unit);
        return true;
    }

    /// <summary>Hand the unit back to its AI orders and return to the command view.</summary>
    public void ReturnToStrategy()
    {
        var unit = PilotedUnit;
        var yaw = Pilot?.LookYaw;
        ReleasePilotedUnit();

        Input.MouseMode = Input.MouseModeEnum.Visible;
        Director?.ShowStrategy(unit is not null && IsInstanceValid(unit) ? unit.GlobalPosition : null, yaw);

        if (Selection is not null)
        {
            Selection.Enabled = true;
            if (unit is not null && IsInstanceValid(unit) && unit.IsAlive) Selection.Select(new[] { unit });
        }

        CurrentMode = Mode.Strategy;
        EmitSignal(SignalName.ModeChanged, (int)CurrentMode, default(Variant));
    }

    public void CyclePilotView()
    {
        if (PilotedUnit is null || Director is null) return;
        ShowView(Director.FollowViewIndex + 1);
    }

    private void ShowView(int index)
    {
        if (Director is null || PilotedUnit is null || Pilot is null) return;
        var rig = Director.Follow(new PilotCameraSubject(PilotedUnit, Pilot), index);
        _pilotView = Director.FollowViewIndex;
        Pilot.BodyFollowsLook = !rig.FreeLook;
    }

    private void ReleasePilotedUnit()
    {
        if (PilotedUnit is not null && IsInstanceValid(PilotedUnit))
        {
            PilotedUnit.Died -= OnPilotedUnitDied;
            PilotedUnit.EndPilot();
        }
        PilotedUnit = null;
        Pilot = null;
    }

    private void OnPilotedUnitDied(Unit unit) => Callable.From(ReturnToStrategy).CallDeferred();

    private void FocusOnPlayerForces()
    {
        if (Director is null) return;
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var unit in UnitRegistry.OnTeam(GetTree(), PlayerTeam))
        {
            sum += unit.GlobalPosition;
            count++;
        }
        Director.ShowStrategy(count > 0 ? sum / count : null, instant: true);
    }
}

/// <summary>Lets a follow camera track a piloted unit: body from the unit, look angles from the pilot.</summary>
public sealed class PilotCameraSubject(Unit unit, PilotController pilot) : ICameraSubject
{
    public Node3D Body => unit;
    public float EyeHeight => unit.EyeHeight;
    public float LookYaw => pilot.LookYaw;
    public float LookPitch => pilot.LookPitch;
    public void SetBodyHidden(bool hidden) => unit.SetBodyHidden(hidden);
}

public static class HybridActions
{
    public const string PilotToggle = "dof_pilot_toggle";
    public const string Cancel = "dof_cancel";
    public const string CycleView = "dof_cycle_view";

    public static void EnsureDefaults()
    {
        InputDefaults.Keys(PilotToggle, Key.F);
        InputDefaults.Keys(Cancel, Key.Escape);
        InputDefaults.Keys(CycleView, Key.V);
    }
}
