# Prototyping (`dof_prototyping`)

Greybox builders: a trooper unit and a battlefield with ground, sun, sky and cover, all made in code.

**Depends on:** dof_combat dof_navigation dof_units

For tests and early prototypes: `Prototype.CreateTrooper(name, team, color)` (a `UnitDefinition`, `Prototype.Trooper`), `Prototype.CreateUnit(definition, name, team, color)` for any unit type on the prototype collision layers, and `Prototype.BuildBattlefield(root)` (bakes a navmesh by default; pass `navigation: false` to skip). Replace with authored scenes once art exists.
