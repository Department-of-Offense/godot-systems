using CommandEngine.Cameras;
using CommandEngine.Controllers;
using CommandEngine.Strategy;
using CommandEngine.UI;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.Core;

/// <summary>
/// The heart of the hybrid loop. In <see cref="Mode.Strategy"/> the player
/// commands units from above; pressing the pilot key on a selected unit
/// switches to <see cref="Mode.Pilot"/> and the player drives that unit
/// directly in first-person, chase or orbit view. Every other unit keeps
/// carrying out its orders the whole time.
///
/// Drop one of these into a battlefield scene. It creates the camera,
/// selection and HUD nodes it needs unless you assign your own.
/// </summary>
[GlobalClass]
public partial class GameModeManager : Node
{
    public enum Mode { Strategy, Pilot }

    [Signal] public delegate void ModeChangedEventHandler(Mode mode, Unit? pilotedUnit);

    [Export] public int PlayerTeam { get; set; }
    [Export] public CameraDirector? Director { get; set; }
    [Export] public SelectionManager? Selection { get; set; }
    [Export] public bool CreateDefaultHud { get; set; } = true;

    /// <summary>Pilot view to start in: 0 first person, 1 chase, 2 orbit.</summary>
    [Export] public int DefaultPilotView { get; set; } = 1;

    public Mode CurrentMode { get; private set; } = Mode.Strategy;
    public Unit? PilotedUnit { get; private set; }
    public PilotController Pilot { get; } = new();

    private int? _pilotView;

    public override void _Ready()
    {
        InputActions.EnsureDefaults();

        if (Director is null)
        {
            Director = new CameraDirector { Name = "CameraDirector" };
            AddChild(Director);
        }

        var overlay = new CanvasLayer { Name = "CommandOverlay" };
        AddChild(overlay);

        if (Selection is null)
        {
            Selection = new SelectionManager { Name = "Selection", PlayerTeam = PlayerTeam };
            overlay.AddChild(Selection);
        }
        Selection.Camera = Director.Camera;

        if (CreateDefaultHud) overlay.AddChild(new CommandHud { Name = "Hud", Modes = this });

        // Start over the player's forces once they've entered the tree.
        Callable.From(FocusOnPlayerForces).CallDeferred();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (CurrentMode == Mode.Strategy)
        {
            if (@event.IsActionPressed(InputActions.PilotToggle) && Selection?.Primary is { } unit)
            {
                TryPilot(unit);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (@event.IsActionPressed(InputActions.PilotToggle) || @event.IsActionPressed(InputActions.Cancel))
        {
            ReturnToStrategy();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(InputActions.CycleCamera))
        {
            CyclePilotView();
            GetViewport().SetInputAsHandled();
        }
        else
        {
            Pilot.HandleInput(@event);
        }
    }

    /// <summary>Take direct control of <paramref name="unit"/>.</summary>
    public bool TryPilot(Unit unit)
    {
        if (!IsInstanceValid(unit) || !unit.IsAlive || unit.Team != PlayerTeam || Director is null) return false;
        if (PilotedUnit == unit) return true;
        if (PilotedUnit is not null) ReleasePilotedUnit();

        PilotedUnit = unit;
        unit.BeginPilot(Pilot);
        unit.Died += OnPilotedUnitDied;

        if (Selection is not null) Selection.Enabled = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Director.ShowPilotView(unit, Pilot, _pilotView ?? DefaultPilotView);

        CurrentMode = Mode.Pilot;
        EmitSignal(SignalName.ModeChanged, (int)CurrentMode, unit);
        return true;
    }

    /// <summary>Hand the unit back to its AI orders and return to the command view.</summary>
    public void ReturnToStrategy()
    {
        var unit = PilotedUnit;
        ReleasePilotedUnit();

        Input.MouseMode = Input.MouseModeEnum.Visible;
        Director?.ShowStrategy(
            unit is not null && IsInstanceValid(unit) ? unit.GlobalPosition : null,
            unit is not null ? Pilot.LookYaw : null);

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
        _pilotView = (Director.PilotViewIndex + 1) % Director.PilotViews.Count;
        Director.ShowPilotView(PilotedUnit, Pilot, _pilotView.Value);
    }

    private void ReleasePilotedUnit()
    {
        if (PilotedUnit is null) return;
        if (IsInstanceValid(PilotedUnit))
        {
            PilotedUnit.Died -= OnPilotedUnitDied;
            PilotedUnit.EndPilot();
        }
        PilotedUnit = null;
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
        if (count > 0) Director.Strategy.Focus = sum / count;
        Director.SetRig(Director.Strategy, instant: true);
    }
}
