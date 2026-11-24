using System.Collections.Generic;
using System.Linq;
using CommandEngine.Core;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.Strategy;

/// <summary>
/// Strategy-mode mouse handling: click or drag-box to select friendly units,
/// right-click to order them. Clicks are resolved in the physics step so the
/// world raycast is safe.
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

    public Camera3D? Camera { get; set; }

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
    public Unit? Primary => _selected.FirstOrDefault(u => IsInstanceValid(u) && u.IsAlive);

    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _dragNow;
    private PendingClick? _pending;

    private sealed record PendingClick(Vector2 Screen, bool IsOrder, bool Shift, bool Ctrl);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled || Camera is null) return;

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
                if (@event.IsActionPressed(InputActions.Stop))
                    foreach (var unit in LiveSelection()) unit.Commands.Clear();
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_pending is null || Camera is null) return;
        var click = _pending;
        _pending = null;

        var hit = RaycastFromScreen(click.Screen);
        if (hit is null) return;
        var (point, unit) = hit.Value;

        if (click.IsOrder)
            IssueOrder(point, unit, click.Shift, click.Ctrl);
        else
            ClickSelect(unit, click.Shift);
    }

    public override void _Draw()
    {
        if (!_dragging || _dragStart.DistanceTo(_dragNow) < DragThresholdPixels) return;
        var rect = new Rect2(_dragStart, _dragNow - _dragStart).Abs();
        DrawRect(rect, new Color(BoxColor, 0.12f));
        DrawRect(rect, BoxColor, filled: false, width: 1.5f);
    }

    public void Select(IEnumerable<Unit> units, bool additive = false)
    {
        if (!additive) ClearSelection(notify: false);
        foreach (var unit in units)
        {
            if (!unit.IsAlive || unit.Team != PlayerTeam || _selected.Contains(unit)) continue;
            _selected.Add(unit);
            unit.IsSelected = true;
        }
        EmitSignal(SignalName.SelectionChanged);
    }

    public void ClearSelection(bool notify = true)
    {
        foreach (var unit in _selected)
            if (IsInstanceValid(unit)) unit.IsSelected = false;
        _selected.Clear();
        if (notify) EmitSignal(SignalName.SelectionChanged);
    }

    /// <summary>Order every selected unit. <paramref name="target"/> set means attack that unit.</summary>
    public void IssueOrder(Vector3 point, Unit? target, bool append, bool attackMove)
    {
        var units = LiveSelection().ToList();
        if (units.Count == 0) return;

        if (target is not null && target.Team != PlayerTeam)
        {
            foreach (var unit in units) unit.Issue(new AttackCommand(target), append);
            EmitSignal(SignalName.OrderIssued, "attack", target.GlobalPosition);
            return;
        }

        foreach (var (unit, slot) in Formation.Assign(units, point, FormationSpacing))
            unit.Issue(attackMove ? new AttackMoveCommand(slot) : new MoveCommand(slot), append);
        EmitSignal(SignalName.OrderIssued, attackMove ? "attack_move" : "move", point);
    }

    private IEnumerable<Unit> LiveSelection()
    {
        _selected.RemoveAll(u => !IsInstanceValid(u) || !u.IsAlive);
        return _selected;
    }

    private void ClickSelect(Unit? unit, bool additive)
    {
        if (unit is null || unit.Team != PlayerTeam)
        {
            if (!additive) ClearSelection();
            return;
        }

        if (additive && _selected.Contains(unit))
        {
            unit.IsSelected = false;
            _selected.Remove(unit);
            EmitSignal(SignalName.SelectionChanged);
            return;
        }
        Select(new[] { unit }, additive);
    }

    private void BoxSelect(Rect2 rect, bool additive)
    {
        var inBox = UnitRegistry.OnTeam(GetTree(), PlayerTeam)
            .Where(u => Camera!.IsPositionInFrustum(u.AimPoint) && rect.HasPoint(Camera.UnprojectPosition(u.AimPoint)))
            .ToList();
        Select(inBox, additive);
    }

    private (Vector3 Point, Unit? Unit)? RaycastFromScreen(Vector2 screen)
    {
        var origin = Camera!.ProjectRayOrigin(screen);
        var end = origin + Camera.ProjectRayNormal(screen) * 2000f;
        var hit = Camera.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(origin, end));
        if (hit.Count == 0) return null;
        return ((Vector3)hit["position"], UnitRegistry.FromCollider(hit["collider"].AsGodotObject()));
    }
}
