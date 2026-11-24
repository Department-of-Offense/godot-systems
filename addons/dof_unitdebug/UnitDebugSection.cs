using System;
using System.Collections.Generic;
using System.Text;
using Dof.Debug;
using Dof.Units;
using Godot;

namespace Dof.UnitDebug;

/// <summary>
/// Debug overlay view of every live unit. Add it to a
/// <see cref="DebugOverlay"/> with <c>overlay.AddSection(new UnitDebugSection())</c>.
///
/// In the world (each can be switched off in the panel):
/// - <b>Paths:</b> the navmesh route to the current order (cyan). Grey when
///   walking straight with no navmesh. A magenta ring marks a stuck unit.
/// - <b>Orders:</b> where each queued order ends, joined in order. Green is
///   move, orange attack-move, red attack.
/// - <b>Targets:</b> a red line from each AI unit to the enemy it's tracking.
/// - <b>Labels:</b> name, health, order, ammo (once used) and stuck time over
///   the units nearest the camera.
/// - <b>Ranges</b> (off by default): engage range and weapon range.
/// - <b>Avoidance</b> (off by default): each unit's avoidance radius.
///
/// In the panel: per-team counts of alive, ordered, engaging and stuck units.
/// </summary>
public sealed class UnitDebugSection : IDebugSection
{
    public string Title => "Units";

    /// <summary>Most labels drawn at once (nearest the camera first).</summary>
    public int MaxLabels { get; set; } = 40;

    /// <summary>No labels on units farther than this from the camera, in metres.</summary>
    public float LabelDistance { get; set; } = 80f;

    /// <summary>A unit counts as stuck once it has made no progress for this long.</summary>
    public float StuckWarningSeconds { get; set; } = 1f;

    /// <summary>Colour for a team's labels and ranges. Replace it to match your game.</summary>
    public Func<int, Color> TeamColor { get; set; } = DefaultTeamColor;

    /// <summary>Which units to show. Null shows all of them.</summary>
    public Func<Unit, bool>? Filter { get; set; }

    public DebugToggle Paths { get; private set; } = null!;
    public DebugToggle Orders { get; private set; } = null!;
    public DebugToggle Targets { get; private set; } = null!;
    public DebugToggle Labels { get; private set; } = null!;
    public DebugToggle Ranges { get; private set; } = null!;
    public DebugToggle Avoidance { get; private set; } = null!;

    /// <summary>Labels drawn last frame.</summary>
    public int LabelsDrawn { get; private set; }

    public static readonly Color PathColor = new(0.3f, 0.9f, 1f);
    public static readonly Color StraightColor = new(0.7f, 0.7f, 0.7f, 0.7f);
    public static readonly Color StuckColor = new(1f, 0.2f, 1f);
    public static readonly Color MoveColor = new(0.35f, 1f, 0.45f);
    public static readonly Color AttackMoveColor = new(1f, 0.65f, 0.2f);
    public static readonly Color AttackColor = new(1f, 0.25f, 0.2f);
    public static readonly Color TargetColor = new(1f, 0.3f, 0.25f, 0.8f);
    public static readonly Color AvoidanceColor = new(1f, 0.95f, 0.4f, 0.6f);

    private DebugOverlay? _overlay;
    private readonly List<(Unit unit, Vector3 position, float distanceSq)> _labelCandidates = new();

    public void Attach(DebugOverlay overlay)
    {
        _overlay = overlay;
        Paths = overlay.AddToggle("Paths");
        Orders = overlay.AddToggle("Orders");
        Targets = overlay.AddToggle("Targets");
        Labels = overlay.AddToggle("Labels");
        Ranges = overlay.AddToggle("Ranges", on: false);
        Avoidance = overlay.AddToggle("Avoidance", on: false);
    }

    public void Draw(double delta)
    {
        _labelCandidates.Clear();
        LabelsDrawn = 0;
        if (!(Paths.On || Orders.On || Targets.On || Labels.On || Ranges.On || Avoidance.On)) return;
        var camera = Labels.On && _overlay?.IsInsideTree() == true ? _overlay.GetViewport().GetCamera3D() : null;
        var eye = camera?.GlobalPosition ?? Vector3.Zero; // once per frame: engine calls add up at 200+ units
        var labelDistanceSq = LabelDistance * LabelDistance;

        foreach (var unit in UnitRegistry.All())
        {
            if (Filter is not null && !Filter(unit)) continue;
            // Draw where the unit is rendered, not where physics last put it.
            var position = unit.GetGlobalTransformInterpolated().Origin;
            var ground = position + Vector3.Up * 0.05f;

            if (Paths.On) DrawPath(unit, ground);
            if (Orders.On) DrawOrders(unit, ground);
            if (Targets.On && !unit.IsPiloted && unit.CurrentEnemy is { } enemy)
                DebugDraw.Line(Chest(unit, position), Chest(enemy, enemy.GetGlobalTransformInterpolated().Origin), TargetColor);
            if (Ranges.On && unit.Weapon is not null)
            {
                var color = TeamColor(unit.Team);
                DebugDraw.Circle(ground, unit.EngageRange, new Color(color, 0.5f), 48);
                DebugDraw.Circle(ground, unit.Weapon.Range, new Color(color, 0.2f), 48);
            }
            if (Avoidance.On && unit.Avoidance.IsAttached)
                DebugDraw.Circle(ground, unit.Avoidance.Radius, AvoidanceColor, 16);

            if (camera is not null)
            {
                var distanceSq = eye.DistanceSquaredTo(position);
                if (distanceSq <= labelDistanceSq) _labelCandidates.Add((unit, position, distanceSq));
            }
        }

        if (_labelCandidates.Count == 0) return;
        _labelCandidates.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));
        for (var i = 0; i < _labelCandidates.Count && i < MaxLabels; i++)
        {
            var (unit, position, _) = _labelCandidates[i];
            DebugDraw.Text(position + Vector3.Up * (unit.EyeHeight + 0.7f), LabelFor(unit), TeamColor(unit.Team));
            LabelsDrawn++;
        }
    }

    public void Write(StringBuilder text)
    {
        var teams = new SortedDictionary<int, TeamStats>();
        var straight = 0;
        string? piloted = null;
        foreach (var unit in UnitRegistry.All())
        {
            if (Filter is not null && !Filter(unit)) continue;
            if (!teams.TryGetValue(unit.Team, out var stats)) stats = new TeamStats();
            stats.Alive++;
            stats.HealthFraction += unit.Health.Fraction;
            if (unit.Commands.Count > 0) stats.Ordered++;
            if (!unit.IsPiloted && unit.CurrentEnemy is not null) stats.Engaging++;
            if (IsStuck(unit)) stats.Stuck++;
            teams[unit.Team] = stats;
            if (unit.Commands.Current?.Destination is not null && !unit.Path.UsingNavmesh) straight++;
            if (unit.IsPiloted) piloted = unit.Name;
        }

        if (teams.Count == 0)
        {
            text.AppendLine("no units");
            return;
        }
        foreach (var (team, s) in teams)
            text.AppendLine($"team {team}: {s.Alive} alive   {s.Ordered} ordered   {s.Engaging} engaging   {s.Stuck} stuck   hp {s.HealthFraction / s.Alive * 100f:0}%");
        if (straight > 0) text.AppendLine($"{straight} walking straight (no navmesh path)");
        if (piloted is not null) text.AppendLine($"piloted: {piloted}");
    }

    private struct TeamStats
    {
        public int Alive, Ordered, Engaging, Stuck;
        public float HealthFraction;
    }

    private bool IsStuck(Unit unit) =>
        unit.Commands.Current?.Destination is not null && unit.Path.StuckSeconds >= StuckWarningSeconds;

    private void DrawPath(Unit unit, Vector3 ground)
    {
        if (unit.Commands.Current?.Destination is not { } destination) return;
        var corners = unit.Path.Corners;
        if (!unit.Path.UsingNavmesh || corners.Length == 0)
        {
            DebugDraw.Line(ground, destination, StraightColor);
        }
        else
        {
            var index = Math.Clamp(unit.Path.CornerIndex, 0, corners.Length - 1);
            DebugDraw.Line(ground, Lift(corners[index]), PathColor);
            for (var i = index + 1; i < corners.Length; i++)
                DebugDraw.Line(Lift(corners[i - 1]), Lift(corners[i]), PathColor);
        }
        if (IsStuck(unit)) DebugDraw.Circle(ground, 0.9f, StuckColor, 16);
    }

    private static void DrawOrders(Unit unit, Vector3 ground)
    {
        Vector3? previous = null;
        foreach (var command in unit.Commands.Items)
        {
            if (command.Destination is not { } destination) continue;
            var color = command switch
            {
                AttackCommand => AttackColor,
                AttackMoveCommand => AttackMoveColor,
                MoveCommand => MoveColor,
                _ => Colors.White,
            };
            var end = Lift(destination);
            // The first leg is shown by the path; later legs join waypoint to waypoint.
            if (previous is { } from) DebugDraw.Arrow(from, end, new Color(color, 0.6f));
            else if (command is AttackCommand) DebugDraw.Line(ground, end, color);
            DebugDraw.Cross(end, 0.6f, color);
            previous = end;
        }
    }

    private string LabelFor(Unit unit)
    {
        // Short lines, so labels on units standing side by side don't run together.
        var text = new StringBuilder();
        text.Append(unit.Name).Append('\n');
        text.Append($"{unit.Health.Current:0}hp ");
        var current = unit.Commands.Current;
        if (unit.IsPiloted) text.Append("piloted");
        else if (current is not null) text.Append(current.Name);
        else text.Append(unit.CurrentEnemy is not null ? "engaging" : "idle");
        if (unit.Commands.Count > 1) text.Append($" +{unit.Commands.Count - 1}");

        // Ammo only once it matters: not full, or reloading.
        if (unit.Weapon is { } weapon && !weapon.HasBottomlessMagazine)
        {
            if (weapon.IsReloading) text.Append($" reload {weapon.ReloadProgress * 100f:0}%");
            else if (weapon.Ammo < weapon.MagazineSize) text.Append($" {weapon.Ammo}/{weapon.MagazineSize}");
        }

        if (IsStuck(unit)) text.Append($"\nstuck {unit.Path.StuckSeconds:0.0}s");
        return text.ToString();
    }

    private static Vector3 Chest(Unit unit, Vector3 position) => position + Vector3.Up * (unit.EyeHeight * 0.7f);
    private static Vector3 Lift(Vector3 point) => point + Vector3.Up * 0.1f;

    public static Color DefaultTeamColor(int team) => team switch
    {
        0 => new Color(0.45f, 0.75f, 1f),
        1 => new Color(1f, 0.5f, 0.45f),
        2 => new Color(0.5f, 1f, 0.55f),
        3 => new Color(1f, 0.9f, 0.4f),
        _ => new Color(0.8f, 0.8f, 0.8f),
    };
}
