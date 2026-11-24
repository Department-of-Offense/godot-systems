# dof_debug

F3 developer overlay for any game, with or without units. Depends only on `dof_core`.

Add a `DebugOverlay` anywhere in the scene and press F3 (`dof_debug_toggle`) to open it. It keeps working while the game is paused.

- **Panel:** live sections, refreshed a few times a second. `PerformanceSection` is built in and shows fps, process and physics time, the worst frame of the last second, nodes, objects, draw calls, active bodies, C# heap and GC counts.
- **Your own sections:** implement `IDebugSection` (`Title`, `Write`, and optionally `Attach` and `Draw`) and call `overlay.AddSection(...)`. A section that throws is switched off with an error, so one bug can't take the overlay down.
- **Toggles:** `overlay.AddToggle("Name", on)` adds a checkbox to the panel and returns a `DebugToggle` whose `On` you read.

## DebugDraw

Immediate-mode drawing from any system: `Line`, `Path`, `Circle` (flat), `Cross`, `Arrow` and `Text`, all in world space. A shape shows for one frame. Pass `seconds` to keep it longer, e.g. for a shot or an impact. Lines draw on top of the scene, through walls.

With no open overlay every call returns immediately, so calls can stay in shipping code. Check `DebugDraw.Enabled` before building expensive debug strings.

Cost: nothing while closed. While open, lines are sent to the engine in one batch, and texts are pooled Labels that only redo their layout when the text changes.
