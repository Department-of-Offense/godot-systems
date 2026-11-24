# Command Engine

A reusable Godot 4 (C#) framework for **hybrid strategy / shooter** games. Zoomed out, you command units like an RTS. Select a unit, press **F**, and you *become* it: first-person, over-the-shoulder chase cam, or a free orbit cam. Every other unit carries on with its orders the whole time. Press **F** again to return to command.

This repo holds the engine layer only. Games (starting with [Fractured Horizons](https://github.com/Department-of-Offense/fractured-horizons)) vendor the addon and add their own units, maps and rules on top.

## Try it

Requirements: [Godot 4.7 .NET](https://godotengine.org/download) and the [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer.

```sh
git clone https://github.com/Department-of-Offense/command-engine.git
cd command-engine
dotnet build
godot --path .            # or open project.godot in the editor and press F5
```

Blue squad is yours, red is hostile.

| Command mode | |
|---|---|
| LMB / drag | Select units / box select |
| RMB | Move (in formation) or attack the clicked enemy |
| Ctrl + RMB | Attack-move: fight anything met on the way |
| Shift + order | Queue the order after the current ones |
| H | Stop: clear orders |
| WASD, Q/E, wheel, MMB drag | Pan, rotate, zoom, tilt |
| **F** | **Pilot the selected unit** |

| Pilot mode | |
|---|---|
| WASD, mouse, Space | Move, look, jump |
| LMB | Fire |
| V | Cycle view: first person → chase → orbit |
| F / Esc | Hand the unit back to its orders and return to command |

Dev shortcut: `godot --path . -- --pilot-view=1` starts already piloting (0 first person, 1 chase, 2 orbit).

## How it fits together

```
addons/command_engine/
  Units/        Unit, Health, Weapon, UnitIntent, commands and the command queue
  Controllers/  CommandController (AI runs the queue), PilotController (player input)
  Cameras/      CameraDirector + rigs: Strategy, FirstPerson, Chase, Orbit
  Strategy/     SelectionManager (click/box select, orders), Formation
  Core/         GameModeManager (the Strategy <-> Pilot switch), InputActions
  UI/           CommandHud (placeholder HUD)
  Prototyping/  Greybox unit and battlefield builders
  Testing/      Headless test runner games can reuse
demo/           Playable sandbox scene
tests/          Engine tests
```

The core idea: **a unit never knows who is driving it.** Every physics tick it asks its controller for a `UnitIntent` (move direction, facing, aim point, fire, jump) and applies it. With no pilot, the controller is the AI `CommandController`, working through the unit's `CommandQueue`. When the player possesses the unit, a `PilotController` builds the same intent from mouse and keyboard, and the queue waits. When the pilot leaves, the AI picks the queue back up. A networked controller can slot in the same way later.

`GameModeManager` is the one node a battlefield scene needs. It creates the camera director, selection handling and HUD unless you supply your own, and registers default input bindings for any `ce_*` action your project's Input Map doesn't already define.

### Adding a command

```csharp
public sealed class PatrolCommand(Vector3 a, Vector3 b) : UnitCommand
{
    private bool _toB = true;
    public override string Name => "Patrol";
    public override bool Execute(Unit unit, double delta, ref UnitIntent intent)
    {
        if (SteerTowards(unit, _toB ? b : a, unit.ArrivalRadius, ref intent)) _toB = !_toB;
        return false; // never finishes on its own
    }
}
```

## Tests

```sh
dotnet build
godot --headless --path . --import
godot --headless --fixed-fps 60 --path . res://tests/TestRunner.tscn            # all
godot --headless --fixed-fps 60 --path . res://tests/TestRunner.tscn -- --filter=Attack
```

`--fixed-fps 60` simulates time instead of waiting on the clock, so the whole suite runs in about a second. Tests are `public async Task Name(TestContext ctx)` methods marked `[Test]`; each gets a fresh world. CI runs the same commands on every push and PR.

## Using it in a game

Games vendor `addons/command_engine` at a tagged release rather than using a submodule, so every game builds with no extra setup. See the game repo's `tools/sync-engine.sh`.

## Roadmap

- Navigation (NavigationAgent3D pathing around cover)
- Unit types beyond the trooper: vehicles and aircraft, each with its own pilot rig
- Squads and control groups, waypoint UI, minimap
- Damage model: armour, hit zones, projectile weapons
- Networking: authoritative server with a networked controller per unit

## Licence

Copyright © 2026 Department of Offense. All rights reserved. See [LICENSE](LICENSE). Public for collaboration, not yet open source.
