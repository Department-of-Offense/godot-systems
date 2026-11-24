# Combat (`dof_combat`)

Health, weapons (hitscan or projectile), teams and damage for anything that implements ICombatant.

**Depends on:** dof_core

Add a `Health` child to anything destructible and implement `ICombatant` (Team + Health) on its root. `Weapon.TryFire(shooter, team, origin, direction, spreadMultiplier)` handles everything about a shot. Override `Health.ModifyDamage` for armour, or `Weapon.Discharge` for beams or other shot types.

## Weapon types as data

`WeaponDefinition` is a weapon type as a resource (damage, fire interval, range, friendly fire, tracer). Share one `.tres` between unit types. `CreateWeapon()` makes an independent `Weapon` node, and `ApplyTo(weapon)` retunes an existing one. A `Weapon` in a scene can also point at one through its `Definition` property. `DamagePerSecond` and `ShotsPerSecond` help with balancing and UI. Subclass it and override `CreateWeapon()` to return your own `Weapon` subclass.

## Weapon feel

Every feature is off by default (perfect accuracy, bottomless magazine, hitscan), so turn on only what a weapon needs:

| Group | Fields | What it does |
|---|---|---|
| Accuracy | `SpreadDegrees`, `SpreadPerShot`, `MaxSpreadDegrees`, `SpreadRecovery`, `Pellets` | Shots land in a cone. Each shot widens it (bloom) up to a cap, and it shrinks back over time. `Pellets` fires several shots per pull, like a shotgun. The holder scales spread per shot with `spreadMultiplier`. |
| Recoil | `RecoilPitchDegrees`, `RecoilYawDegrees` | Raised as the `Recoiled` C# event (radians) once per pull. The weapon doesn't move anything itself. A player controller kicks the view, an AI can ignore it. |
| Ammo | `MagazineSize` (0 = bottomless), `ReloadTime`, `AutoReload` | `Ammo`, `IsReloading`, `ReloadProgress`, `StartReload()`, `Refill()`. Signals: `AmmoChanged`, `ReloadStarted`, `ReloadFinished`. |
| Projectile | `ProjectileSpeed` (0 = hitscan), `ProjectileGravity`, `SplashRadius`, `SplashEdgeDamage` | Fires `Projectile`s that travel, drop, and raycast each step so they can't tunnel through walls. Splash damages everything in range, less toward the edge. With splash, they also burst at max range. |
| Aiming | `AdsSpreadMultiplier`, `AdsZoom` | Spread multiplier and camera zoom while aiming down sights. |

The `Hit(target, damage, killed)` signal fires for every hit this weapon deals, projectiles and splash included. It skips targets that friendly-fire rules protect, and targets already dead. Use it for hit markers, kill feeds and stats. `WeaponDefinition` mirrors all of these fields, and its `DamagePerSecond` counts pellets and reload downtime.

You can also spawn a `Projectile` yourself (grenades, turrets): set its fields and add it to the world.
