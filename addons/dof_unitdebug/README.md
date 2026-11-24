# dof_unitdebug

The unit view for the debug overlay. Depends on `dof_debug` and `dof_units`.

```csharp
overlay.AddSection(new UnitDebugSection());
```

Each layer has a checkbox in the panel:

| Layer | Default | Shows |
|---|---|---|
| Paths | on | Navmesh route to the current order (cyan), or grey when walking straight with no navmesh. A magenta ring marks a stuck unit. |
| Orders | on | Where each queued order ends, joined in order. Green is move, orange attack-move, red attack. |
| Targets | on | A red line from each AI unit to the enemy it's tracking (`Unit.CurrentEnemy`, which never triggers a rescan). |
| Labels | on | Name, health, order, ammo once used, and stuck time, over the `MaxLabels` units nearest the camera within `LabelDistance`. |
| Ranges | off | Engage range and weapon range circles. |
| Avoidance | off | Each unit's avoidance radius. |

The panel shows per-team counts of units alive, ordered, engaging and stuck, plus average health. It also flags units walking straight because they have no navmesh path.

Options: `TeamColor` (colours per team), `Filter` (which units to show), `StuckWarningSeconds`, `MaxLabels`, `LabelDistance`.

Cost: with 240 units and every layer on, about 3–5 ms per frame, mostly drawing every unit's full route. Switch layers off, or use `Filter`, to cut it down.
