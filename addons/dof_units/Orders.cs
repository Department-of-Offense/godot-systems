using System.Collections.Generic;
using Dof.Core;
using Godot;

namespace Dof.Units;

/// <summary>
/// An order a unit carries out on its own when no pilot is driving it.
/// Orders are small state machines: <see cref="Execute"/> fills in the
/// unit's intent each tick and returns true once the order is done.
/// Subclass to add game-specific orders (patrol, build, capture, repair...).
/// </summary>
public abstract class UnitCommand
{
    public abstract string Name { get; }
    internal bool Started { get; set; }

    public virtual void OnStart(Unit unit) { }

    /// <returns>True when the command is complete and should be dequeued.</returns>
    public abstract bool Execute(Unit unit, double delta, ref UnitIntent intent);

    /// <summary>World point this order is heading for, for UI markers. Null if none.</summary>
    public virtual Vector3? Destination => null;

    /// <summary>Walk toward <paramref name="target"/>. Returns true once within <paramref name="stopRadius"/>.</summary>
    protected static bool SteerTowards(Unit unit, Vector3 target, float stopRadius, ref UnitIntent intent)
    {
        var to = Math3D.Flat(target - unit.GlobalPosition);
        var distance = to.Length();
        if (distance <= stopRadius) return true;

        var direction = to / distance;
        // Ease off over the last couple of metres so units don't overshoot and jitter.
        intent.Move = direction * Mathf.Clamp(distance / 1.5f, 0.35f, 1f);
        intent.FaceYaw = Math3D.YawFor(direction);
        return false;
    }

    /// <summary>Turn toward <paramref name="target"/> and shoot once roughly facing it.</summary>
    public static void Engage(Unit unit, Unit target, ref UnitIntent intent)
    {
        var to = Math3D.Flat(target.GlobalPosition - unit.GlobalPosition);
        if (to.LengthSquared() > 0.0001f) intent.FaceYaw = Math3D.YawFor(to);
        intent.AimAt = target.AimPoint;
        intent.Fire = unit.IsFacing(target.GlobalPosition, toleranceRadians: 0.35f);
    }
}

public sealed class MoveCommand(Vector3 target) : UnitCommand
{
    public Vector3 Target { get; } = target;
    public override string Name => "Move";
    public override Vector3? Destination => Target;

    public override bool Execute(Unit unit, double delta, ref UnitIntent intent) =>
        SteerTowards(unit, Target, unit.ArrivalRadius, ref intent);
}

/// <summary>Chase a specific target and shoot it until it dies.</summary>
public sealed class AttackCommand(Unit target) : UnitCommand
{
    public Unit Target { get; } = target;
    public override string Name => "Attack";
    public override Vector3? Destination => GodotObject.IsInstanceValid(Target) ? Target.GlobalPosition : null;

    public override bool Execute(Unit unit, double delta, ref UnitIntent intent)
    {
        if (!GodotObject.IsInstanceValid(Target) || !Target.IsAlive || unit.Weapon is null) return true;

        if (unit.GlobalPosition.DistanceTo(Target.GlobalPosition) > unit.EngageRange)
        {
            SteerTowards(unit, Target.GlobalPosition, 0f, ref intent);
            return false;
        }

        Engage(unit, Target, ref intent);
        return false;
    }
}

/// <summary>Move to a point, but stop to fight any enemy that comes into weapon range on the way.</summary>
public sealed class AttackMoveCommand(Vector3 target) : UnitCommand
{
    public Vector3 Target { get; } = target;
    public override string Name => "Attack-move";
    public override Vector3? Destination => Target;

    public override bool Execute(Unit unit, double delta, ref UnitIntent intent)
    {
        if (unit.Weapon is not null && unit.NearestEnemyInRange() is { } enemy)
        {
            Engage(unit, enemy, ref intent);
            return false;
        }
        return SteerTowards(unit, Target, unit.ArrivalRadius, ref intent);
    }
}

/// <summary>Ordered list of commands. The head is the active one.</summary>
public sealed class CommandQueue
{
    private readonly List<UnitCommand> _items = new();

    public event System.Action? Changed;

    public UnitCommand? Current => _items.Count > 0 ? _items[0] : null;
    public IReadOnlyList<UnitCommand> Items => _items;
    public int Count => _items.Count;

    /// <param name="append">Queue after existing orders (shift-click) instead of replacing them.</param>
    public void Issue(UnitCommand command, bool append = false)
    {
        if (!append) _items.Clear();
        command.Started = false;
        _items.Add(command);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }

    public void Tick(Unit unit, double delta, ref UnitIntent intent)
    {
        var command = Current;
        if (command is null) return;

        if (!command.Started)
        {
            command.Started = true;
            command.OnStart(unit);
        }

        if (command.Execute(unit, delta, ref intent))
        {
            _items.Remove(command);
            Changed?.Invoke();
        }
    }
}
