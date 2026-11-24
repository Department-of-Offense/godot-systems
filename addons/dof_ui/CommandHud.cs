using System.Linq;
using Dof.Combat;
using Dof.Hybrid;
using Dof.Units;
using Godot;

namespace Dof.UI;

/// <summary>
/// Placeholder HUD for the hybrid loop: mode banner, control hints, unit
/// health bars in strategy view; in pilot view a crosshair that opens up
/// with weapon spread, hit markers (red on a kill), ammo and reload.
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
    [Export] public bool ShowHitMarkers { get; set; } = true;
    [Export] public bool ShowAmmo { get; set; } = true;
    [Export] public Color KillMarkerColor { get; set; } = new(1f, 0.25f, 0.2f);
    [Export] public Color FriendlyColor { get; set; } = new(0.3f, 1f, 0.4f);
    [Export] public Color HostileColor { get; set; } = new(1f, 0.3f, 0.25f);

    [Export(PropertyHint.MultilineText)] public string StrategyHelp { get; set; } =
        "LMB select / drag box   RMB move   Ctrl+RMB attack-move   Shift queue   H stop\n" +
        "WASD pan   Q/E rotate   wheel zoom   MMB drag tilt   F pilot selected unit";

    [Export(PropertyHint.MultilineText)] public string PilotHelp { get; set; } =
        "WASD move   mouse look   LMB fire   RMB aim   R reload   Space jump   V cycle view   F / Esc back to command";

    private Label _banner = null!;
    private Label _help = null!;
    private Weapon? _trackedWeapon;
    private double _hitMarkerLeft;
    private bool _hitMarkerKill;

    private const double HitMarkerTime = 0.15;
    private const double KillMarkerTime = 0.35;

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
        Track(piloting ? Modes.PilotedUnit!.Weapon : null);
        if (_hitMarkerLeft > 0) _hitMarkerLeft -= delta;
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
            var centre = GetViewportRect().Size / 2f;
            var unit = Modes.PilotedUnit;
            if (ShowCrosshair) DrawCrosshair(centre, unit is null ? 0f : SpreadPixels(unit));
            if (ShowHitMarkers && _hitMarkerLeft > 0) DrawHitMarker(centre);
            if (ShowAmmo && unit?.Weapon is { } weapon) DrawAmmo(centre, weapon);
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

    /// <summary>Follow the piloted unit's weapon so hit markers fire for its hits.</summary>
    private void Track(Weapon? weapon)
    {
        if (weapon == _trackedWeapon) return;
        if (_trackedWeapon is not null && IsInstanceValid(_trackedWeapon)) _trackedWeapon.Hit -= OnHit;
        _trackedWeapon = weapon;
        if (weapon is not null) weapon.Hit += OnHit;
        _hitMarkerLeft = 0;
    }

    private void OnHit(Node? target, float damage, bool killed)
    {
        // A kill marker isn't cut short by a plain hit in the same burst.
        if (killed || !_hitMarkerKill || _hitMarkerLeft <= 0) _hitMarkerKill = killed;
        _hitMarkerLeft = _hitMarkerKill ? KillMarkerTime : HitMarkerTime;
    }

    /// <summary>True while a hit marker is showing (and whether it's a kill marker). For tests and custom HUDs.</summary>
    public bool HitMarkerVisible => _hitMarkerLeft > 0;
    public bool HitMarkerIsKill => HitMarkerVisible && _hitMarkerKill;

    /// <summary>Radius on screen, in pixels, of the cone the next shot can land in.</summary>
    public float SpreadPixels(Unit unit)
    {
        var camera = GetViewport().GetCamera3D();
        if (unit.Weapon is null || camera is null) return 0f;
        var spread = Mathf.DegToRad(unit.Weapon.CurrentSpreadDegrees(unit.SpreadMultiplier));
        var halfFov = Mathf.DegToRad(camera.Fov) / 2f; // vertical field of view (Godot keeps height by default)
        return Mathf.Tan(spread) / Mathf.Tan(halfFov) * GetViewportRect().Size.Y / 2f;
    }

    private void DrawCrosshair(Vector2 c, float spreadPixels)
    {
        var white = new Color(1, 1, 1, 0.9f);
        var gap = 3f + spreadPixels;
        const float length = 7f;
        DrawLine(c + new Vector2(-gap - length, 0), c + new Vector2(-gap, 0), white, 2);
        DrawLine(c + new Vector2(gap, 0), c + new Vector2(gap + length, 0), white, 2);
        DrawLine(c + new Vector2(0, -gap - length), c + new Vector2(0, -gap), white, 2);
        DrawLine(c + new Vector2(0, gap), c + new Vector2(0, gap + length), white, 2);
        DrawCircle(c, 1.5f, white);
    }

    private void DrawHitMarker(Vector2 c)
    {
        var color = _hitMarkerKill ? KillMarkerColor : Colors.White;
        var size = _hitMarkerKill ? 11f : 8f;
        foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            DrawLine(c + d * 4f, c + d * size, color, _hitMarkerKill ? 3f : 2f);
    }

    private void DrawAmmo(Vector2 c, Weapon weapon)
    {
        var font = ThemeDB.FallbackFont;
        var size = GetViewportRect().Size;
        var text = weapon.HasBottomlessMagazine ? "∞" : $"{weapon.Ammo} / {weapon.MagazineSize}";
        var low = !weapon.HasBottomlessMagazine && weapon.Ammo <= weapon.MagazineSize / 4;
        DrawString(font, new Vector2(size.X - 216, size.Y - 24), text, HorizontalAlignment.Right, 200, 28,
            low ? KillMarkerColor : Colors.White);

        if (!weapon.IsReloading) return;
        var bar = new Rect2(c + new Vector2(-30, 26), new Vector2(60, 4));
        DrawRect(bar, new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * weapon.ReloadProgress, bar.Size.Y)), Colors.White);
        DrawString(font, c + new Vector2(-60, 46), "RELOADING", HorizontalAlignment.Center, 120, 14, Colors.White);
    }
}
