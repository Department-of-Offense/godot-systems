# Units (`dof_units`)

Controllable units. Each tick a unit applies a UnitIntent from whichever controller drives it (AI orders, local pilot, or your own), plus an order queue with move, attack and attack-move.

**Depends on:** dof_core dof_combat dof_navigation

Author a unit as a scene: `Unit` root with a collision shape, meshes, and optional `Health`/`Weapon` children. Give orders with `unit.Issue(new MoveCommand(p), append)`. Orders follow navmesh paths when the world has one (`UseNavigation`, tune via `unit.Path`) and walk straight otherwise. Subclass `UnitCommand` for new orders, implement `IUnitController` for new drivers (network, scripted), set `unit.AiController` for custom AI, and override `OnDeath()` for custom deaths.

## Unit types as data

A `UnitDefinition` resource describes a unit type: name, description, icon, tags, stats (health, speed, turning, jump), body size, a model scene, a `WeaponDefinition`, and AI tuning. Create one in the editor with **New Resource → UnitDefinition**, save it as a `.tres`, and tweak the numbers without touching code.

```csharp
var catalog = UnitCatalog.LoadFolder("res://units");   // every UnitDefinition .tres in the folder
var heavy = catalog.Get("heavy").Spawn(team: 0, teamColor: Colors.Blue);
heavy.Position = spawnPoint;
AddChild(heavy);
```

- **Fills in what's missing.** Stats are copied onto the unit. A collider, model, `Health` and `Weapon` are added only if the unit doesn't have them, and an existing `Health` or `Weapon` is retuned to the definition. So a hand-made unit scene can set `Unit.Definition` just to take its numbers.
- **Per-unit copies.** Changing one unit (an upgrade, a debuff) never changes the shared `.tres` or other units.
- **Models.** `Model` is instanced as the unit's `Model` child, facing -Z. Meshes in the `dof_team_color` group are tinted with the team colour. Without a model the unit gets a team-coloured placeholder capsule sized to `Radius` and `Height`.
- **Catalog.** `Id` is the lookup key, so keep it stable once shipped (save files will use it). Duplicate or empty ids are skipped with a warning. `WithTag("armored")` filters by tag.
- **Your own fields.** Subclass with `[GlobalClass] public partial class MyUnitDefinition : UnitDefinition`, add `[Export]` fields (cost, build time, armour class...), and override `Configure(unit)` (call `base.Configure` first) or `CreateUnit()` to spawn your own `Unit` subclass.

Example definitions live in the systems repo at `demo/units/`.
