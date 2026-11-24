using System.Collections.Generic;
using Dof.Combat;
using Godot;

namespace Dof.Units;

/// <summary>
/// Lookups over every live <see cref="Unit"/>. Units register themselves
/// when they enter the tree and leave on death or exit, so queries walk a
/// plain C# list. (Querying a Godot group from C# copies the whole group
/// across the engine boundary on every call, which made per-unit target
/// scans quadratic in cost.)
/// </summary>
public static class UnitRegistry
{
    /// <summary>Units are also put in this group, for editor tooling and GDScript.</summary>
    public const string Group = "dof_units";

    private static readonly List<Unit> Live = new();

    internal static void Register(Unit unit)
    {
        if (!Live.Contains(unit)) Live.Add(unit);
    }

    internal static void Unregister(Unit unit) => Live.Remove(unit);

    /// <summary>Every live unit. The <paramref name="tree"/> argument is kept for API stability.</summary>
    public static IEnumerable<Unit> All(SceneTree? tree = null)
    {
        for (var i = 0; i < Live.Count; i++)
            if (Live[i].IsAlive) yield return Live[i];
    }

    public static IEnumerable<Unit> OnTeam(SceneTree? tree, int team)
    {
        for (var i = 0; i < Live.Count; i++)
            if (Live[i].Team == team && Live[i].IsAlive) yield return Live[i];
    }

    /// <summary>Nearest hostile live unit within <paramref name="radius"/>, or null. Uncached: prefer <see cref="Unit.NearestEnemyInRange"/>.</summary>
    public static Unit? FindNearestEnemy(Unit from, float radius)
    {
        Unit? best = null;
        var origin = from.GlobalPosition;
        var bestDistSq = radius * radius;
        for (var i = 0; i < Live.Count; i++)
        {
            var other = Live[i];
            if (other == from || !other.IsAlive || !Teams.AreHostile(from.Team, other.Team)) continue;
            var distSq = origin.DistanceSquaredTo(other.GlobalPosition);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = other;
            }
        }
        return best;
    }

    public static Unit? FromCollider(GodotObject? collider) => CombatQuery.FromCollider(collider) as Unit;
}
