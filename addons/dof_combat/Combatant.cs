using Godot;

namespace Dof.Combat;

/// <summary>
/// Anything weapons can hurt: a soldier, a turret, a crate, a building.
/// Implement on the node that owns the collision body (or one of its parents).
/// </summary>
public interface ICombatant
{
    /// <summary>Team id. Use <see cref="Teams.Neutral"/> for things anyone can damage.</summary>
    int Team { get; }
    Health Health { get; }
}

public static class Teams
{
    public const int Neutral = -1;

    public static bool AreHostile(int a, int b) => a != b || a == Neutral;
}

public static class CombatQuery
{
    /// <summary>Walk up from a physics collider to the combatant that owns it, if any.</summary>
    public static ICombatant? FromCollider(GodotObject? collider)
    {
        var node = collider as Node;
        while (node is not null)
        {
            if (node is ICombatant combatant) return combatant;
            node = node.GetParent();
        }
        return null;
    }
}
