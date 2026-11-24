# UI (`dof_ui`)

Placeholder HUD for the hybrid loop: mode banner, control hints and health bars. In pilot view it shows a crosshair that opens up with the weapon's current spread, hit markers (red and larger on a kill), and ammo with a reload bar.

**Depends on:** dof_combat dof_hybrid dof_units

Add a `CommandHud` under a CanvasLayer. It finds the GameModeManager automatically. Toggle parts with the `Show*` exports, or replace it with your own UI built on the same signals.
