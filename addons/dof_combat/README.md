# Combat (`dof_combat`)

Health, hitscan weapons, teams and damage for anything that implements ICombatant.

**Depends on:** dof_core

Add a `Health` child to anything destructible and implement `ICombatant` (Team + Health) on its root. `Weapon.TryFire(shooter, team, origin, direction)` handles cooldown, raycast, friendly fire and tracers. Override `Health.ModifyDamage` for armour, or `Weapon.Discharge` for projectiles, spread or beams.
