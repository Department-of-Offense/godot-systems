using System.Collections.Generic;
using Godot;

namespace CommandEngine.Units;

/// <summary>Lookups over every live <see cref="Unit"/> in the scene tree.</summary>
public static class UnitRegistry
{
    public const string Group = "command_engine_units";

    public static IEnumerable<Unit> All(SceneTree tree)
    {
        foreach (var node in tree.GetNodesInGroup(Group))
            if (node is Unit unit && unit.IsAlive) yield return unit;
    }

    public static IEnumerable<Unit> OnTeam(SceneTree tree, int team)
    {
        foreach (var unit in All(tree))
            if (unit.Team == team) yield return unit;
    }

    public static Unit? FindNearestEnemy(Unit from, float radius)
    {
        Unit? best = null;
        var bestDistSq = radius * radius;
        foreach (var other in All(from.GetTree()))
        {
            if (other == from || other.Team == from.Team) continue;
            var distSq = from.GlobalPosition.DistanceSquaredTo(other.GlobalPosition);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = other;
            }
        }
        return best;
    }

    /// <summary>Walk up from a physics collider to the unit that owns it, if any.</summary>
    public static Unit? FromCollider(GodotObject? collider)
    {
        var node = collider as Node;
        while (node is not null)
        {
            if (node is Unit unit) return unit;
            node = node.GetParent();
        }
        return null;
    }
}
