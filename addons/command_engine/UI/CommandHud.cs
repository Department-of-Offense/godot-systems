using System.Linq;
using CommandEngine.Core;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.UI;

/// <summary>
/// Placeholder HUD: mode banner, control hints, unit health bars in strategy
/// view, crosshair and health in pilot view. Games will replace this with
/// their own UI; it exists so the engine is playable on its own.
/// </summary>
public partial class CommandHud : Godot.Control
{
    public GameModeManager? Modes { get; set; }

    private const string StrategyHelp =
        "LMB select / drag box   RMB move   Ctrl+RMB attack-move   Shift queue   H stop\n" +
        "WASD pan   Q/E rotate   wheel zoom   MMB drag tilt   F pilot selected unit";

    private const string PilotHelp =
        "WASD move   mouse look   LMB fire   Space jump   V cycle view   F / Esc back to command";

    private Label _banner = null!;
    private Label _help = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _banner = new Label { Position = new Vector2(16, 12) };
        _banner.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_banner);

        _help = new Label { Position = new Vector2(16, 44), Modulate = new Color(1, 1, 1, 0.8f) };
        AddChild(_help);
    }

    public override void _Process(double delta)
    {
        if (Modes is null) return;

        if (Modes.CurrentMode == GameModeManager.Mode.Pilot && Modes.PilotedUnit is { } unit)
        {
            var view = Modes.Director?.Active.Name ?? "";
            _banner.Text = $"PILOTING {unit.DisplayName}  [{view}]  HP {unit.Health.Current:0}/{unit.Health.MaxHealth:0}";
            _help.Text = PilotHelp;
        }
        else
        {
            var selected = Modes.Selection?.Selected.Count(u => IsInstanceValid(u) && u.IsAlive) ?? 0;
            _banner.Text = $"COMMAND  {selected} selected";
            _help.Text = StrategyHelp;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Modes is null) return;
        var camera = GetViewport().GetCamera3D();

        if (Modes.CurrentMode == GameModeManager.Mode.Pilot)
        {
            var c = GetViewportRect().Size / 2f;
            var white = new Color(1, 1, 1, 0.9f);
            DrawLine(c + new Vector2(-10, 0), c + new Vector2(-3, 0), white, 2);
            DrawLine(c + new Vector2(3, 0), c + new Vector2(10, 0), white, 2);
            DrawLine(c + new Vector2(0, -10), c + new Vector2(0, -3), white, 2);
            DrawLine(c + new Vector2(0, 3), c + new Vector2(0, 10), white, 2);
            return;
        }

        if (camera is null) return;
        foreach (var unit in UnitRegistry.All(GetTree()))
        {
            var head = unit.GlobalPosition + Vector3.Up * (unit.EyeHeight + 0.6f);
            if (!camera.IsPositionInFrustum(head)) continue;
            var p = camera.UnprojectPosition(head);
            var bar = new Rect2(p - new Vector2(18, 2), new Vector2(36, 4));
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            var color = unit.Team == Modes.PlayerTeam ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.25f);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * unit.Health.Fraction, bar.Size.Y)), color);
        }
    }
}
