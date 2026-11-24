# Godot Systems

Modular, reusable **Godot 4 (C#) gameplay systems** for Department of Offense games. Each system is a self-contained addon folder. A game vendors the ones it needs (dependencies come along automatically) and uses them however it likes: standalone, wired together, subclassed, or partly replaced.

| System | Folder | What it gives you | Depends on |
|---|---|---|---|
| Core | `dof_core` | Math helpers, input-binding defaults | none |
| Testing | `dof_testing` | Headless test runner with simulated time | none |
| Cameras | `dof_cameras` | Camera director: RTS view plus first-person, chase and orbit follow views, blending, spring-arm collision | core |
| Combat | `dof_combat` | Health, hitscan weapons, teams, damage for any `ICombatant` | core |
| Navigation | `dof_navigation` | Runtime navmesh baking and path following for any Node3D, straight-line fallback | core |
| Units | `dof_units` | Controllable units, AI and pilot controllers, order queue (move / attack / attack-move), pathfinding orders | core, combat, navigation |
| Command | `dof_command` | RTS selection (click / box), right-click orders, formations | core, combat, units |
| Hybrid | `dof_hybrid` | The strategy ↔ pilot loop: command from above, press F to possess a unit | core, cameras, command, units |
| UI | `dof_ui` | Placeholder HUD for the hybrid loop | hybrid, units |
| Prototyping | `dof_prototyping` | Greybox unit and battlefield builders (battlefields bake a navmesh) | combat, navigation, units |

Each folder has a `README.md` covering usage and extension points, and a `module.cfg` declaring its dependencies.

## Using systems in a game

1. Copy [`tools/sync-systems.sh`](tools/sync-systems.sh) into the game's `tools/` folder.
2. Pull the systems you want at a tagged release:

   ```sh
   tools/sync-systems.sh v0.2.0 cameras            # just cameras (+ core)
   tools/sync-systems.sh v0.2.0 hybrid ui          # the full strategy/shooter loop
   tools/sync-systems.sh v0.2.0 all                # everything
   tools/sync-systems.sh v0.3.0                    # upgrade, keeping the same set
   ```

3. Commit `addons/dof_*` and `SYSTEMS_VERSION`. The game then builds with no submodules or setup. Don't edit vendored folders in the game: change them here, tag, and re-sync. (Fractured Horizons' CI checks this.)

While developing a system and a game together: `SYSTEMS_PATH=../godot-systems tools/sync-systems.sh` copies from your local checkout. On Windows, run the scripts from Git Bash.

## Built to be used differently per game

The systems give you extension points instead of fixed behaviour:

- **Interfaces at the seams.** Cameras follow any `ICameraSubject` (soldier, vehicle, drone, spectator). Weapons hurt any `ICombatant` (unit, turret, crate). Units accept any `IUnitController` (AI, local pilot, network peer, cutscene script).
- **Subclass to change behaviour.** New orders (`UnitCommand`), camera views (`FollowRig` / `ICameraRig`), weapons (`Weapon.Discharge`), armour (`Health.ModifyDamage`), deaths (`Unit.OnDeath`), selection rules and orders (`SelectionManager.CanSelect` / `CreateGroundOrder` / `CreateUnitOrder`), possession rules (`GameModeManager.CanPilot` / `CreatePilot`).
- **Configure, don't fork.** Nodes are `[GlobalClass]` (they show up in Godot's Add Node dialog) with exported settings. Follow views are an editable list. Every input action name can be overridden, and default key bindings only apply when the game's Input Map doesn't define the action.
- **Glue is optional.** `dof_hybrid` wires cameras, command and units into the strategy/shooter loop. A pure shooter can skip it and use cameras, units and combat directly.

## The rules that keep it modular

- **A module may only use modules listed in its `module.cfg` `depends`.** Godot compiles everything into one assembly, so the compiler won't stop violations. CI does, twice:
  - A test scans each module's `Dof.*` imports against its declared dependencies and rejects cycles.
  - `tools/check-standalone.sh` compiles every module alone with only its dependencies.
- **Nothing game-specific.** Faction names, unit rosters and maps live in game repos.
- **Folders, not files.** A module is a whole system. Split one only when a game actually needs part of it alone.

## Try the demo

Requirements: [Godot 4.7 .NET](https://godotengine.org/download) and the [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer.

```sh
git clone https://github.com/Department-of-Offense/godot-systems.git
cd godot-systems
dotnet build
godot --path .                     # or open project.godot in the editor and press F5
godot --path . -- --pilot-view=1   # start already piloting (0 first person, 1 chase, 2 orbit)
```

You command the blue squad: LMB / drag to select, RMB to move or attack, Ctrl+RMB to attack-move, Shift to queue, H to stop, WASD/Q/E/wheel for the camera. **F** possesses the selected unit (WASD, mouse, LMB fire, Space jump, V cycle view); **F** or **Esc** returns to command.

## Tests

```sh
dotnet build
godot --headless --path . --import
godot --headless --fixed-fps 60 --path . res://tests/TestRunner.tscn             # all
godot --headless --fixed-fps 60 --path . res://tests/TestRunner.tscn -- --filter=Camera
bash tools/check-standalone.sh                                                   # each module alone
```

`--fixed-fps 60` simulates time instead of waiting on the clock, so the suite runs in about a second.

## Roadmap

Navigation (pathing around cover), vehicle and aircraft units with their own controllers and follow views, control groups and minimap, projectile weapons and armour, networking via a networked `IUnitController`.

## Licence

Copyright © 2026 Department of Offense. All rights reserved. See [LICENSE](LICENSE). Public for collaboration, not yet open source.
