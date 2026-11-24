# Hybrid (`dof_hybrid`)

The strategy-to-pilot loop: command from above, press F to possess a selected unit in a follow view, press F to return. Glue over cameras, command and units.

**Depends on:** dof_core dof_cameras dof_command dof_units

Add a `GameModeManager` to a battlefield scene. It creates a `CameraDirector` and `SelectionManager` unless you assign your own. Override `CanPilot` to restrict which units can be possessed, and `CreatePilot` to give different units different controls. While the pilot aims down sights, the follow view zooms by the weapon's `AdsZoom`. Override `AimZoom` to change that, e.g. stronger zoom in first person.
