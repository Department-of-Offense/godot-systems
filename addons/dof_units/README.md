# Units (`dof_units`)

Controllable units. Each tick a unit applies a UnitIntent from whichever controller drives it (AI orders, local pilot, or your own), plus an order queue with move, attack and attack-move.

**Depends on:** dof_core dof_combat dof_navigation

Author a unit as a scene: `Unit` root with a collision shape, meshes, and optional `Health`/`Weapon` children. Give orders with `unit.Issue(new MoveCommand(p), append)`. Orders follow navmesh paths when the world has one (`UseNavigation`, tune via `unit.Path`) and walk straight otherwise. Subclass `UnitCommand` for new orders, implement `IUnitController` for new drivers (network, scripted), set `unit.AiController` for custom AI, and override `OnDeath()` for custom deaths.
