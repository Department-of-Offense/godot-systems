# Navigation (`dof_navigation`)

Pathfinding on Godot's built-in navigation server: runtime navmesh baking from static collision, plus a throttled path follower for any Node3D, with straight-line fallback.

**Depends on:** dof_core

## Getting a navmesh

- **Levels built in code or changed at runtime:** add a `NavmeshBaker` node. It parses the StaticBody3D colliders under `SourceRoot` (default: its parent) and bakes on ready. Moving bodies like units are never carved out. Call `Bake()` again after changing static geometry. Set `BakeAsync` for big maps so the frame doesn't hitch.
- **Hand-authored levels:** a normal `NavigationRegion3D` baked in the editor works just as well.

Set the agent size (`AgentRadius`, `AgentHeight`, `AgentMaxClimb`) to match your movers, with `AgentRadius` a little larger than their collider.

`CellSize` (default 0.25 m) trades bake time for accuracy. A 160 m map bakes in roughly 0.3–0.5 s at the default. Don't go much coarser than the agent radius: Recast simplifies outlines by up to ~1.3 cells, and at 0.5 m cells paths cut corners tightly enough that a 0.4 m capsule snags on them.

## Following paths

```csharp
var path = new PathFollower();                 // one per agent
// each physics tick:
var next = path.NextPoint(agent, target);      // steer toward this point
var arrived = agent.GlobalPosition.DistanceTo(path.Goal) < 0.5f;
```

- `Goal` is where the agent can actually get to: the target, or the nearest reachable point if the target is inside an obstacle.
- Re-plans happen every `RepathInterval` seconds (spread across agents) or straight away when the target moves more than `TargetMoveTolerance`.
- With no navmesh in the world, `NextPoint` returns the target itself (straight-line movement).
- `Corners` and `CornerIndex` expose the current path, e.g. for debug drawing.

`dof_units` uses this for every order that moves a unit (`Unit.Path`, `Unit.UseNavigation`).

## Not yet

Local avoidance between moving agents (units still push each other physically), off-mesh links (jumps, ladders), and flow fields for very large groups.
