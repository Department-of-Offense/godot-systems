# Command (`dof_command`)

RTS command layer: click and box selection, right-click orders, formations, stop.

**Depends on:** dof_core dof_combat dof_units

Add a `SelectionManager` under a CanvasLayer and set `PlayerTeam`. Subclass it and override `CanSelect`, `CreateGroundOrder`, `CreateUnitOrder` or `SetMarker` to change the rules or look, or listen to `SelectionChanged` / `OrderIssued`.
