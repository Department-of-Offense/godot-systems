using System.Threading.Tasks;
using Dof.Core;
using Dof.Navigation;
using Dof.Prototyping;
using Dof.Testing;
using Dof.Units;
using Godot;

namespace Dof.Tests;

public class NavigationTests
{
    /// <summary>A 30 m wall across the path, 6 m in front of the start.</summary>
    private static void Wall(TestContext ctx, Vector3 center, Vector3 size)
    {
        var wall = new StaticBody3D { Position = center };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        ctx.Add(wall);
    }

    private static async Task<NavmeshBaker> ArenaWithWall(TestContext ctx, bool navigation = true)
    {
        var baker = Prototype.BuildBattlefield(ctx.World, size: 80f, coverCount: 0, navigation: navigation);
        Wall(ctx, new Vector3(0, 1.5f, -6), new Vector3(30, 3, 1));
        if (baker is not null)
        {
            await ctx.World.ToSignal(baker, NavmeshBaker.SignalName.Baked);
        }
        return baker!;
    }

    private static Unit Spawn(TestContext ctx, Vector3 at)
    {
        var unit = Prototype.CreateTrooper("Walker", 0, Colors.White);
        unit.Position = at;
        return ctx.Add(unit);
    }

    [Test]
    public async Task UnitsPathAroundAWall(TestContext ctx)
    {
        await ArenaWithWall(ctx);
        var unit = Spawn(ctx, new Vector3(0, 0.1f, 0));
        var target = new Vector3(0, 0, -12);
        unit.Issue(new MoveCommand(target));

        await ctx.PhysicsFrames(5);
        Check.True(unit.Path.UsingNavmesh, "path planned on the navmesh");
        Check.True(unit.Path.Corners.Length >= 3, $"path bends around the wall ({unit.Path.Corners.Length} corners)");

        await ctx.Until(() => unit.Commands.Count == 0, 60 * 20, "unit to reach the far side of the wall");
        Check.True(Math3D.Flat(unit.GlobalPosition - target).Length() < 1f, $"arrived at {unit.GlobalPosition}");
    }

    [Test]
    public async Task WithoutNavigationTheWallBlocks(TestContext ctx)
    {
        // Control for the test above: proves the wall really is in the way.
        await ArenaWithWall(ctx, navigation: false);
        var unit = Spawn(ctx, new Vector3(0, 0.1f, 0));
        unit.Issue(new MoveCommand(new Vector3(0, 0, -12)));
        await ctx.PhysicsFrames(60 * 6);
        Check.True(unit.Commands.Count == 1, "straight-line unit is still stuck behind the wall");
        Check.True(!unit.Path.UsingNavmesh, "no navmesh, so the follower fell back to straight lines");
    }

    [Test]
    public async Task TargetsInsideObstaclesEndAtTheNearestReachablePoint(TestContext ctx)
    {
        var baker = Prototype.BuildBattlefield(ctx.World, size: 60f, coverCount: 0);
        Wall(ctx, new Vector3(0, 1.5f, -10), new Vector3(6, 3, 6)); // solid block, target at its centre
        await ctx.World.ToSignal(baker!, NavmeshBaker.SignalName.Baked);

        var unit = Spawn(ctx, new Vector3(0, 0.1f, 0));
        unit.Issue(new MoveCommand(new Vector3(0, 0, -10)));
        await ctx.Until(() => unit.Commands.Count == 0, 60 * 10, "move into a solid block to complete anyway");
        // Nearest reachable point is anywhere just outside the 6 m block (3 m + agent radius from its centre).
        var fromCentre = Math3D.Flat(unit.GlobalPosition - new Vector3(0, 0, -10));
        Check.True(Mathf.Abs(fromCentre.X) >= 3f || Mathf.Abs(fromCentre.Z) >= 3f, $"stopped outside the block, at {unit.GlobalPosition}");
        Check.True(fromCentre.Length() < 5.5f, $"stopped next to the block, not far away ({fromCentre.Length():F1} m)");
    }

    [Test]
    public async Task PathFollowerWorksForAnyNode(TestContext ctx)
    {
        await ArenaWithWall(ctx);
        var drone = ctx.Add(new Node3D { Position = new Vector3(0, 0, 0) });
        var follower = new PathFollower();
        var target = new Vector3(0, 0, -12);

        var next = follower.NextPoint(drone, target);
        Check.True(follower.UsingNavmesh, "plain Node3D got a navmesh path");
        Check.True(Math3D.Flat(next - target).Length() > 1f, $"first steer point is a corner, not the target ({next})");
        Check.True(Mathf.Abs(next.X) > 10f, $"corner goes around the 30 m wall's end ({next})");
    }

    [Test]
    public async Task NoNavmeshMeansStraightLines(TestContext ctx)
    {
        var drone = ctx.Add(new Node3D());
        await ctx.PhysicsFrames(2);
        var follower = new PathFollower();
        var target = new Vector3(5, 0, -5);
        Check.Equal(target, follower.NextPoint(drone, target), "steer point with no navmesh");
        Check.True(!follower.UsingNavmesh, "reports straight-line fallback");
    }
}

public class AvoidanceTests
{
    [Test]
    public async Task CrowdsThroughAGapDontClump(TestContext ctx)
    {
        // Two blocks leave a 4 m gap; 16 units are sent through it to a row
        // of separate spots. With avoidance they spread out instead of
        // stacking into one blob at the gap's corners.
        var baker = Prototype.BuildBattlefield(ctx.World, size: 80f, coverCount: 0);
        foreach (var x in new[] { -8f, 8f })
        {
            var block = new StaticBody3D { Position = new Vector3(x, 1.5f, -8) };
            block.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(12, 3, 2) } });
            ctx.Add(block);
        }
        await ctx.World.ToSignal(baker!, NavmeshBaker.SignalName.Baked);

        var units = new System.Collections.Generic.List<Unit>();
        for (var i = 0; i < 16; i++)
        {
            var unit = Prototype.CreateTrooper($"U{i}", 0, Colors.White);
            unit.Position = new Vector3((i % 8 - 3.5f) * 2f, 0.1f, 2f + i / 8 * 2f);
            units.Add(ctx.Add(unit));
        }
        await ctx.PhysicsFrames(1);
        for (var i = 0; i < units.Count; i++)
            units[i].Issue(new MoveCommand(new Vector3((i % 8 - 3.5f) * 2f, 0, -20f - i / 8 * 2f)));

        var closest = float.MaxValue;
        for (var f = 0; f < 60 * 12; f++)
        {
            await ctx.PhysicsFrames(1);
            for (var a = 0; a < units.Count; a++)
                for (var b = a + 1; b < units.Count; b++)
                    closest = Mathf.Min(closest, Math3D.Flat(units[a].GlobalPosition - units[b].GlobalPosition).Length());
            if (units.TrueForAll(u => u.Commands.Count == 0)) break;
        }

        var arrived = units.FindAll(u => u.Commands.Count == 0).Count;
        Check.True(arrived == units.Count, $"all units got through the gap ({arrived}/{units.Count})");
        // Colliders are 0.4 m radius: 0.8 m apart means touching. Clumps overlap-push to ~0.8.
        Check.True(closest > 0.75f, $"units kept their spacing (closest pair {closest:F2} m)");
    }

    [Test]
    public async Task LiveUnitsAreAvoidedAndDeadOnesAreNot(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 60f, coverCount: 0, navigation: false);
        var unit = ctx.Add(Prototype.CreateTrooper("P", 0, Colors.White));
        await ctx.PhysicsFrames(2);
        Check.True(unit.Avoidance.IsAttached, "units register an avoidance agent");
        unit.Health.TakeDamage(10000f);
        await ctx.PhysicsFrames(1);
        Check.True(!unit.Avoidance.IsAttached, "dead units stop being avoided");
    }
}
