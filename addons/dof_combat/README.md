# Combat (`dof_combat`)

Health, hitscan weapons, teams and damage for anything that implements ICombatant.

**Depends on:** dof_core

Add a `Health` child to anything destructible and implement `ICombatant` (Team + Health) on its root. `Weapon.TryFire(shooter, team, origin, direction)` handles cooldown, raycast, friendly fire and tracers. Override `Health.ModifyDamage` for armour, or `Weapon.Discharge` for projectiles, spread or beams.

## Weapon types as data

`WeaponDefinition` is a weapon type as a resource (damage, fire interval, range, friendly fire, tracer). Share one `.tres` between unit types. `CreateWeapon()` makes an independent `Weapon` node, and `ApplyTo(weapon)` retunes an existing one. A `Weapon` in a scene can also point at one through its `Definition` property. `DamagePerSecond` and `ShotsPerSecond` help with balancing and UI. Subclass it and override `CreateWeapon()` to return your own `Weapon` subclass.
