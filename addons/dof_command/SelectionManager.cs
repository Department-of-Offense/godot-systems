using System.Collections.Generic;
using System.Linq;
using Dof.Combat;
using Dof.Core;
using Dof.Units;
using Godot;

namespace Dof.Command;

/// <summary>
/// RTS-style mouse handling: click or drag-box to select friendly units,
/// right-click to order them. Clicks are resolved in the physics step so the
/// world raycast is safe.
///
/// Games customise behaviour by subclassing and overriding
/// <see cref="CanSelect"/>, <see cref="CreateGroundOrder"/> and
/// <see cref="CreateUnitOrder"/>, or by listening to the signals.
/// </summary>
[GlobalClass]
public partial class SelectionManager : Godot.Control
{
    [Signal] public delegate void SelectionChangedEventHandler();
    [Signal] public delegate void OrderIssuedEventHandler(string order, Vector3 point);

    [Export] public int PlayerTeam { get; set; }
    [Export] public float FormationSpacing { get; set; } = 2.2f;
    [Export] public float DragThresholdPixels { get; set; } = 6f;
    [Export] public Color BoxColor { get; set; } = new(0.3f, 1f, 0.4f);
    [Export] public Color MarkerColor { get; set; } = new(0.3f, 1f, 0.4f);
    [Export] public string StopAction { get; set; } = CommandActions.Stop;

    /// <summary>Camera used for picking. Defaults to the viewport's active camera.</summary>
    [Export] public Camera3D? Camera { get; set; }

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _dragging = false;
            QueueRedraw();
        }
    }

    private readonly List<Unit> _selected = new();
    public IReadOnlyList<Unit> Selected => _selected;
    public Unit? Primary => LiveSelection().FirstOrDefault();

    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _dragNow;
    private PendingClick? _pending;

    private sealed record PendingClick(Vector2 Screen, bool IsOrder, bool Shift, bool Ctrl);
    private const string MarkerName = "DofSelectionMarker";

    public override void _Ready()
    {
        CommandActions.EnsureDefaults();
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    private Camera3D? PickCamera => Camera ?? GetViewport().GetCamera3D();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled || PickCamera is null) return;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                if (left.Pressed)
                {
                    _dragging = true;
                    _dragStart = _dragNow = left.Position;
                }
                else if (_dragging)
                {
                    _dragging = false;
                    if (_dragStart.DistanceTo(left.Position) >= DragThresholdPixels)
                        BoxSelect(new Rect2(_dragStart, left.Position - _dragStart).Abs(), left.ShiftPressed);
                    else
                        _pending = new PendingClick(left.Position, false, left.ShiftPressed, false);
                    QueueRedraw();
                }
                break;

            case InputEventMouseMotion motion when _dragging:
                _dragNow = motion.Position;
                QueueRedraw();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right:
                _pending = new PendingClick(right.Position, true, right.ShiftPressed, right.CtrlPressed);
                break;

            default:
                if (@event.IsActionPressed(StopAction))
                    foreach (var unit in LiveSelection()) unit.Commands.Clear();
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Drop units that died while selected.
        if (_selected.RemoveAll(u => !IsInstanceValid(u) || !u.IsAlive) > 0)
            EmitSignal(SignalName.SelectionChanged);

        if (_pending is null || PickCamera is null) return;
        var click = _pending;
        _pending = null;

        var hit = RaycastFromScreen(click.Screen);
        if (hit is null) return;
        var (point, unit) = hit.Value;

        if (click.IsOrder) IssueOrder(point, unit, click.Shift, click.Ctrl);
        else ClickSelect(unit, click.Shift);
    }

    public override void _Draw()
    {
        if (!_dragging || _dragStart.DistanceTo(_dragNow) < DragThresholdPixels) return;
        var rect = new Rect2(_dragStart, _dragNow - _dragStart).Abs();
        DrawRect(rect, new Color(BoxColor, 0.12f));
        DrawRect(rect, BoxColor, filled: false, width: 1.5f);
    }

    /// <summary>Which units the player may select. Default: live units on <see cref="PlayerTeam"/>.</summary>
    public virtual bool CanSelect(Unit unit) => unit.IsAlive && unit.Team == PlayerTeam;

    /// <summary>Order for a right-click on the ground. <paramref name="slot"/> is the unit's formation spot.</summary>
    protected virtual UnitCommand CreateGroundOrder(Unit unit, Vector3 slot, bool attackModifier) =>
        attackModifier ? new AttackMoveCommand(slot) : new MoveCommand(slot);

    /// <summary>Order for a right-click on another unit. Return null to treat it as a ground click.</summary>
    protected virtual UnitCommand? CreateUnitOrder(Unit unit, Unit clicked) =>
        Teams.AreHostile(unit.Team, clicked.Team) ? new AttackCommand(clicked) : null;

    public void Select(IEnumerable<Unit> units, bool additive = false)
    {
        if (!additive) ClearSelection(notify: false);
        foreach (var unit in units)
        {
            if (!CanSelect(unit) || _selected.Contains(unit)) continue;
            _selected.Add(unit);
            SetMarker(unit, true);
        }
        EmitSignal(SignalName.SelectionChanged);
    }

    public void Deselect(Unit unit)
    {
        if (!_selected.Remove(unit)) return;
        SetMarker(unit, false);
        EmitSignal(SignalName.SelectionChanged);
    }

    public void ClearSelection(bool notify = true)
    {
        foreach (var unit in _selected) SetMarker(unit, false);
        _selected.Clear();
        if (notify) EmitSignal(SignalName.SelectionChanged);
    }

    /// <summary>Order every selected unit, as if right-clicking <paramref name="point"/> / <paramref name="clicked"/>.</summary>
    public void IssueOrder(Vector3 point, Unit? clicked, bool append, bool attackModifier)
    {
        var units = LiveSelection().ToList();
        if (units.Count == 0) return;

        if (clicked is not null)
        {
            var targeted = units.Select(u => (Unit: u, Order: CreateUnitOrder(u, clicked))).Where(x => x.Order is not null).ToList();
            if (targeted.Count > 0)
            {
                foreach (var (unit, order) in targeted) unit.Issue(order!, append);
                EmitSignal(SignalName.OrderIssued, targeted[0].Order!.Name, clicked.GlobalPosition);
                return;
            }
        }

        string? name = null;
        foreach (var (unit, slot) in Formation.Assign(units, point, FormationSpacing))
        {
            var order = CreateGroundOrder(unit, slot, attackModifier);
            name ??= order.Name;
            unit.Issue(order, append);
        }
        EmitSignal(SignalName.OrderIssued, name ?? "", point);
    }

    private IEnumerable<Unit> LiveSelection()
    {
        _selected.RemoveAll(u => !IsInstanceValid(u) || !u.IsAlive);
        return _selected;
    }

    private void ClickSelect(Unit? unit, bool additive)
    {
        if (unit is null || !CanSelect(unit))
        {
            if (!additive) ClearSelection();
            return;
        }
        if (additive && _selected.Contains(unit)) Deselect(unit);
        else Select(new[] { unit }, additive);
    }

    private void BoxSelect(Rect2 rect, bool additive)
    {
        var camera = PickCamera!;
        var inBox = UnitRegistry.All(GetTree())
            .Where(u => CanSelect(u) && camera.IsPositionInFrustum(u.AimPoint) && rect.HasPoint(camera.UnprojectPosition(u.AimPoint)))
            .ToList();
        Select(inBox, additive);
    }

    private (Vector3 Point, Unit? Unit)? RaycastFromScreen(Vector2 screen)
    {
        var camera = PickCamera!;
        var origin = camera.ProjectRayOrigin(screen);
        var end = origin + camera.ProjectRayNormal(screen) * 2000f;
        var hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, end));
        if (hit.Count == 0) return null;
        return ((Vector3)hit["position"], UnitRegistry.FromCollider(hit["collider"].AsGodotObject()));
    }

    /// <summary>Ring under selected units. Override for a custom look.</summary>
    protected virtual void SetMarker(Unit unit, bool selected)
    {
        if (!IsInstanceValid(unit)) return;
        var existing = unit.GetNodeOrNull<Node3D>(MarkerName);
        if (!selected)
        {
            existing?.QueueFree();
            return;
        }
        if (existing is not null) return;

        unit.AddChild(new MeshInstance3D
        {
            Name = MarkerName,
            Mesh = new TorusMesh { InnerRadius = 0.68f, OuterRadius = 0.8f, Rings = 32 },
            Position = new Vector3(0f, 0.05f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = MarkerColor,
            },
        });
    }
}

public static class CommandActions
{
    public const string Stop = "dof_stop";

    public static void EnsureDefaults() => InputDefaults.Keys(Stop, Key.H);
}
