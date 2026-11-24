# Navigation (`dof_navigation`)

Pathfinding and local avoidance on Godot's built-in navigation server, for any Node3D: runtime navmesh baking from static collision, a throttled path follower with straight-line fallback, and RVO crowd avoidance.

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

- `StuckSeconds` is how long since the agent last got meaningfully closer to `Goal`: use it to settle for "close enough" or give up.

## Avoiding each other

Paths tell agents where to go. Without avoidance, a crowd that follows the same path funnels through the same corners into a clump. `AvoidanceAgent` adds RVO steering:

```csharp
var avoid = new AvoidanceAgent { Radius = 0.5f, MaxSpeed = 6f };
avoid.Attach(GetWorld3D());                    // on enter tree; Detach() on exit/death
// each physics tick, after deciding the velocity you want:
velocity = avoid.Steer(GlobalPosition, desiredVelocity);
```

- An agent ignores agents with lower `Priority`. Give movers a higher priority than standing agents, and a player-controlled agent the highest.
- Standing agents (desired velocity zero) step aside for higher-priority movers once the solver asks for at least `YieldSpeed`. Without this, a line of standing agents can wall movers off.
- `Steer(..., obey: false)` returns your velocity unchanged but keeps the agent visible to others: use it for player control.
- The safe velocity lags one physics tick.

`dof_units` uses all of this by default (`Unit.Path`, `Unit.Avoidance`, `UseNavigation`, `UseAvoidance`).

## Not yet

Off-mesh links (jumps, ladders), avoidance against static obstacles (agents rely on the navmesh and physics for walls), and flow fields for very large groups.
