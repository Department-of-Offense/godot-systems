using System.Linq;
using System.Threading.Tasks;
using CommandEngine.Cameras;
using CommandEngine.Core;
using CommandEngine.Prototyping;
using CommandEngine.Strategy;
using CommandEngine.Testing;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.Tests;

public class UnitCommandTests
{
    private static Unit Spawn(TestContext ctx, string name, int team, Vector3 at)
    {
        var unit = Prototype.CreateTrooper(name, team, Colors.White);
        unit.Position = at;
        return ctx.Add(unit);
    }

    private static void Ground(TestContext ctx) => Prototype.BuildBattlefield(ctx.World, size: 80f, coverCount: 0);

    [Test]
    public async Task MoveCommandReachesTarget(TestContext ctx)
    {
        Ground(ctx);
        var unit = Spawn(ctx, "Mover", 0, new Vector3(0, 0.1f, 0));
        var target = new Vector3(8, 0, -6);
        unit.Issue(new MoveCommand(target));

        await ctx.Until(() => unit.Commands.Count == 0, 600, "move command to complete");
        var distance = UnitMath.Flat(unit.GlobalPosition - target).Length();
        Check.True(distance <= unit.ArrivalRadius + 0.05f, $"unit stopped {distance:0.00} m from target");
    }

    [Test]
    public async Task QueuedCommandsRunInOrder(TestContext ctx)
    {
        Ground(ctx);
        var unit = Spawn(ctx, "Queuer", 0, new Vector3(0, 0.1f, 0));
        unit.Issue(new MoveCommand(new Vector3(5, 0, 0)));
        unit.Issue(new MoveCommand(new Vector3(5, 0, 5)), append: true);
        Check.Equal(2, unit.Commands.Count, "queued command count");

        await ctx.Until(() => unit.Commands.Count == 1, 600, "first leg to complete");
        Check.True(unit.GlobalPosition.X > 4f && unit.GlobalPosition.Z < 1f, $"after leg one unit is at {unit.GlobalPosition}");

        await ctx.Until(() => unit.Commands.Count == 0, 600, "second leg to complete");
        Check.True(unit.GlobalPosition.Z > 4f, $"after leg two unit is at {unit.GlobalPosition}");
    }

    [Test]
    public async Task IssuingWithoutAppendReplacesOrders(TestContext ctx)
    {
        Ground(ctx);
        var unit = Spawn(ctx, "Replacer", 0, new Vector3(0, 0.1f, 0));
        unit.Issue(new MoveCommand(new Vector3(20, 0, 0)));
        unit.Issue(new MoveCommand(new Vector3(20, 0, 20)), append: true);
        unit.Issue(new MoveCommand(new Vector3(-3, 0, 0)));
        Check.Equal(1, unit.Commands.Count, "command count after replace");
        await ctx.Until(() => unit.Commands.Count == 0, 600, "replacement order to complete");
        Check.True(unit.GlobalPosition.X < 0f, $"unit went to the replacement target, now at {unit.GlobalPosition}");
    }

    [Test]
    public async Task AttackCommandKillsTarget(TestContext ctx)
    {
        Ground(ctx);
        var attacker = Spawn(ctx, "Attacker", 0, new Vector3(0, 0.1f, 0));
        var victim = Spawn(ctx, "Victim", 1, new Vector3(0, 0.1f, -36));
        victim.Ai.AutoEngageWhenIdle = false; // a fair fight is a different test

        attacker.Issue(new AttackCommand(victim));
        await ctx.Until(() => !victim.IsAlive, 60 * 30, "victim to die");
        Check.True(attacker.IsAlive, "attacker survived");
        Check.True(attacker.GlobalPosition.DistanceTo(new Vector3(0, 0, -36)) < attacker.Weapon!.Range,
            "attacker closed to weapon range");
    }

    [Test]
    public async Task IdleUnitsReturnFire(TestContext ctx)
    {
        Ground(ctx);
        var blue = Spawn(ctx, "Blue", 0, new Vector3(0, 0.1f, 0));
        var red = Spawn(ctx, "Red", 1, new Vector3(0, 0.1f, -15));
        await ctx.Until(() => blue.Health.Current < 100f && red.Health.Current < 100f, 600, "both sides to trade fire");
    }

    [Test]
    public async Task AttackMoveStopsToFight(TestContext ctx)
    {
        Ground(ctx);
        var unit = Spawn(ctx, "Sweeper", 0, new Vector3(0, 0.1f, 0));
        var blocker = Spawn(ctx, "Blocker", 1, new Vector3(0, 0.1f, -20));
        blocker.Ai.AutoEngageWhenIdle = false;

        unit.Issue(new AttackMoveCommand(new Vector3(0, 0, -60)));
        await ctx.Until(() => !blocker.IsAlive, 60 * 30, "blocker to die on the way");
        Check.Equal(1, unit.Commands.Count, "attack-move still active after the fight");
    }

    [Test]
    public async Task DeadUnitsLeaveTheRegistry(TestContext ctx)
    {
        var unit = Spawn(ctx, "Doomed", 0, Vector3.Zero);
        await ctx.PhysicsFrames(1);
        var died = false;
        unit.Died += _ => died = true;
        unit.Health.TakeDamage(1000f);
        Check.True(died, "Died signal fired");
        Check.True(!UnitRegistry.All(ctx.Tree).Contains(unit), "dead unit is no longer listed");
    }
}

public class FormationTests
{
    [Test]
    public Task GridSlotsAreDistinctAndCentred(TestContext ctx)
    {
        var slots = Formation.Grid(new Vector3(10, 0, 10), Vector3.Forward, 7, 2f);
        Check.Equal(7, slots.Count, "slot count");
        for (var i = 0; i < slots.Count; i++)
            for (var j = i + 1; j < slots.Count; j++)
                Check.True(slots[i].DistanceTo(slots[j]) >= 1.99f, $"slots {i} and {j} overlap");

        var centroid = slots.Aggregate(Vector3.Zero, (a, b) => a + b) / slots.Count;
        Check.True(centroid.DistanceTo(new Vector3(10, 0, 10)) < 1.5f, $"formation centred near target, got {centroid}");
        return Task.CompletedTask;
    }
}

public class ModeSwitchTests
{
    [Test]
    public async Task PilotAndReturnPreservesOrders(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 80f, coverCount: 0);
        var unit = Prototype.CreateTrooper("Pilotable", 0, Colors.White);
        unit.Position = new Vector3(0, 0.1f, 0);
        ctx.Add(unit);
        var modes = ctx.Add(new GameModeManager { PlayerTeam = 0 });
        await ctx.PhysicsFrames(2);

        unit.Issue(new MoveCommand(new Vector3(30, 0, 0)));
        Check.True(modes.TryPilot(unit), "pilot accepted");
        Check.Equal(GameModeManager.Mode.Pilot, modes.CurrentMode, "mode after pilot");
        Check.True(unit.IsPiloted, "unit reports piloted");

        var start = unit.GlobalPosition;
        await ctx.PhysicsFrames(30);
        Check.True(unit.GlobalPosition.DistanceTo(start) < 0.2f, "AI orders are paused while piloted and no keys are held");
        Check.Equal(1, unit.Commands.Count, "orders kept while piloted");

        modes.ReturnToStrategy();
        Check.Equal(GameModeManager.Mode.Strategy, modes.CurrentMode, "mode after return");
        Check.True(!unit.IsPiloted, "unit released");
        await ctx.PhysicsFrames(30);
        Check.True(unit.GlobalPosition.X > start.X + 1f, "unit resumed its order after the pilot left");
    }

    [Test]
    public async Task CyclingPilotViewsChangesRig(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0);
        var unit = ctx.Add(Prototype.CreateTrooper("Viewer", 0, Colors.White));
        var modes = ctx.Add(new GameModeManager { PlayerTeam = 0, DefaultPilotView = 0 });
        await ctx.PhysicsFrames(2);

        modes.TryPilot(unit);
        Check.True(modes.Director!.Active is FirstPersonRig, "starts in first person");
        modes.CyclePilotView();
        Check.True(modes.Director.Active is ChaseRig, "then chase");
        modes.CyclePilotView();
        Check.True(modes.Director.Active is OrbitRig, "then orbit");
        Check.True(!modes.Pilot.BodyFollowsLook, "orbit lets the body face its movement");
        modes.CyclePilotView();
        Check.True(modes.Director.Active is FirstPersonRig, "wraps back to first person");
    }

    [Test]
    public async Task PilotedUnitDeathReturnsToStrategy(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0);
        var unit = ctx.Add(Prototype.CreateTrooper("Brave", 0, Colors.White));
        var modes = ctx.Add(new GameModeManager { PlayerTeam = 0 });
        await ctx.PhysicsFrames(2);

        modes.TryPilot(unit);
        unit.Health.TakeDamage(1000f);
        await ctx.PhysicsFrames(2);
        Check.Equal(GameModeManager.Mode.Strategy, modes.CurrentMode, "mode after pilot death");
    }

    [Test]
    public async Task ChaseCameraPullsInFrontOfWalls(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0);
        // Unit faces -Z, so the chase camera sits behind it on +Z. Put a wall there.
        var wall = new StaticBody3D { Position = new Vector3(0, 2, 2.5f) };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(10, 4, 0.5f) } });
        ctx.Add(wall);
        var unit = ctx.Add(Prototype.CreateTrooper("Cornered", 0, Colors.White));
        var modes = ctx.Add(new GameModeManager { PlayerTeam = 0, DefaultPilotView = 1 });
        await ctx.PhysicsFrames(2);

        modes.TryPilot(unit);
        await ctx.PhysicsFrames(3);
        var chase = modes.Director!.Chase;
        Check.True(chase.ArmFraction < 0.6f, $"arm pulled in by the wall, fraction {chase.ArmFraction:0.00}");
        var cameraZ = chase.Evaluate(0).Origin.Z;
        Check.True(cameraZ < 2.25f - chase.CollisionRadius + 0.05f, $"camera stays in front of the wall, z={cameraZ:0.00}");
    }

    [Test]
    public async Task CannotPilotEnemyUnits(TestContext ctx)
    {
        var enemy = ctx.Add(Prototype.CreateTrooper("Enemy", 1, Colors.Red));
        var modes = ctx.Add(new GameModeManager { PlayerTeam = 0 });
        await ctx.PhysicsFrames(1);
        Check.True(!modes.TryPilot(enemy), "enemy unit refused");
    }
}
