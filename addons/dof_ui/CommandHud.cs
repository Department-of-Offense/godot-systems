using System.Linq;
using Dof.Hybrid;
using Dof.Units;
using Godot;

namespace Dof.UI;

/// <summary>
/// Placeholder HUD for the hybrid loop: mode banner, control hints, unit
/// health bars in strategy view, crosshair and health in pilot view.
/// Add it under a CanvasLayer. It finds the scene's GameModeManager on its
/// own. Each part can be switched off, so a game can keep only what it needs.
/// </summary>
[GlobalClass]
public partial class CommandHud : Godot.Control
{
    [Export] public GameModeManager? Modes { get; set; }
    [Export] public bool ShowBanner { get; set; } = true;
    [Export] public bool ShowHelp { get; set; } = true;
    [Export] public bool ShowHealthBars { get; set; } = true;
    [Export] public bool ShowCrosshair { get; set; } = true;
    [Export] public Color FriendlyColor { get; set; } = new(0.3f, 1f, 0.4f);
    [Export] public Color HostileColor { get; set; } = new(1f, 0.3f, 0.25f);

    [Export(PropertyHint.MultilineText)] public string StrategyHelp { get; set; } =
        "LMB select / drag box   RMB move   Ctrl+RMB attack-move   Shift queue   H stop\n" +
        "WASD pan   Q/E rotate   wheel zoom   MMB drag tilt   F pilot selected unit";

    [Export(PropertyHint.MultilineText)] public string PilotHelp { get; set; } =
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
        Modes ??= GetTree().GetFirstNodeInGroup(GameModeManager.Group) as GameModeManager;
        if (Modes is null) return;

        var piloting = Modes.CurrentMode == GameModeManager.Mode.Pilot && Modes.PilotedUnit is not null;
        if (piloting)
        {
            var unit = Modes.PilotedUnit!;
            var view = Modes.Director?.Active.Name ?? "";
            _banner.Text = $"PILOTING {unit.DisplayName}  [{view}]  HP {unit.Health.Current:0}/{unit.Health.MaxHealth:0}";
        }
        else
        {
            var selected = Modes.Selection?.Selected.Count(u => IsInstanceValid(u) && u.IsAlive) ?? 0;
            _banner.Text = $"COMMAND  {selected} selected";
        }
        _banner.Visible = ShowBanner;
        _help.Text = piloting ? PilotHelp : StrategyHelp;
        _help.Visible = ShowHelp;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Modes is null) return;

        if (Modes.CurrentMode == GameModeManager.Mode.Pilot)
        {
            if (ShowCrosshair) DrawCrosshair(GetViewportRect().Size / 2f);
            return;
        }

        var camera = GetViewport().GetCamera3D();
        if (!ShowHealthBars || camera is null) return;
        foreach (var unit in UnitRegistry.All(GetTree()))
        {
            var head = unit.GlobalPosition + Vector3.Up * (unit.EyeHeight + 0.6f);
            if (!camera.IsPositionInFrustum(head)) continue;
            var bar = new Rect2(camera.UnprojectPosition(head) - new Vector2(18, 2), new Vector2(36, 4));
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            var color = unit.Team == Modes.PlayerTeam ? FriendlyColor : HostileColor;
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * unit.Health.Fraction, bar.Size.Y)), color);
        }
    }

    private void DrawCrosshair(Vector2 c)
    {
        var white = new Color(1, 1, 1, 0.9f);
        DrawLine(c + new Vector2(-10, 0), c + new Vector2(-3, 0), white, 2);
        DrawLine(c + new Vector2(3, 0), c + new Vector2(10, 0), white, 2);
        DrawLine(c + new Vector2(0, -10), c + new Vector2(0, -3), white, 2);
        DrawLine(c + new Vector2(0, 3), c + new Vector2(0, 10), white, 2);
    }
}
