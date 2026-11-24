using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dof.Cameras;
using Dof.Combat;
using Dof.Hybrid;
using Dof.Prototyping;
using Dof.Testing;
using Dof.UI;
using Dof.Units;
using Godot;

namespace Dof.Tests;

/// <summary>A bare target: a 1 x 2 x 1 m static box with health.</summary>
public partial class TargetDummy : StaticBody3D, ICombatant
{
    public int Team { get; set; } = 1;
    public Health Health { get; private set; } = null!;
    public float MaxHealth { get; set; } = 1000f;

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1, 2, 1) }, Position = new Vector3(0, 1, 0) });
        Health = new Health { MaxHealth = MaxHealth };
        AddChild(Health);
    }

    public float DamageTaken => Health.MaxHealth - Health.Current;
}

/// <summary>Drives a unit with a fixed intent, to test how units apply intents.</summary>
public sealed class FixedIntentController(UnitIntent intent) : IUnitController
{
    public UnitIntent Intent = intent;
    public UnitIntent ComputeIntent(Unit unit, double delta) => Intent;
}

public class WeaponFeelTests
{
    private static Weapon AddWeapon(TestContext ctx, Weapon weapon)
    {
        weapon.ShowTracers = false;
        return ctx.Add(weapon);
    }

    private static TargetDummy AddDummy(TestContext ctx, Vector3 at, float health = 1000f) =>
        ctx.Add(new TargetDummy { Position = at, MaxHealth = health });

    // Fire every physics frame for a number of frames; returns shots fired.
    private static async Task<int> HoldTrigger(TestContext ctx, Weapon weapon, int frames, Vector3 origin, Vector3 direction, float spreadMultiplier = 1f)
    {
        var shots = 0;
        for (var i = 0; i < frames; i++)
        {
            if (weapon.TryFire(null, 0, origin, direction, spreadMultiplier)) shots++;
            await ctx.PhysicsFrames(1);
        }
        return shots;
    }

    [Test]
    public async Task MagazinesEmptyThenAutoReload(TestContext ctx)
    {
        var weapon = AddWeapon(ctx, new Weapon { MagazineSize = 3, ReloadTime = 0.5f, FireInterval = 0.05f });
        await ctx.PhysicsFrames(1);
        Check.Equal(3, weapon.Ammo, "starts full");

        var shots = await HoldTrigger(ctx, weapon, 18, Vector3.Up * 5, Vector3.Up); // 0.3 s, aiming at the sky
        Check.Equal(3, shots, "only a magazine's worth of shots");
        Check.True(weapon.IsReloading, "reloads automatically when empty");
        Check.True(weapon.ReloadProgress is > 0f and < 1f, $"reload progress shows ({weapon.ReloadProgress:F2})");

        await ctx.PhysicsFrames(30);
        Check.True(!weapon.IsReloading, "reload finished after ReloadTime");
        Check.Equal(3, weapon.Ammo, "magazine full again");
    }

    [Test]
    public async Task ManualReloadOnlyWhenItHelps(TestContext ctx)
    {
        var weapon = AddWeapon(ctx, new Weapon { MagazineSize = 10, ReloadTime = 0.2f });
        await ctx.PhysicsFrames(1);
        Check.True(!weapon.StartReload(), "no reload with a full magazine");
        weapon.TryFire(null, 0, Vector3.Up * 5, Vector3.Up);
        Check.True(weapon.StartReload(), "reload after a shot");
        Check.True(!weapon.TryFire(null, 0, Vector3.Up * 5, Vector3.Up), "can't fire while reloading");
        var bottomless = AddWeapon(ctx, new Weapon());
        await ctx.PhysicsFrames(1);
        Check.True(!bottomless.StartReload(), "bottomless weapons never reload");
    }

    [Test]
    public async Task SpreadBloomsWithFireAndRecovers(TestContext ctx)
    {
        var weapon = AddWeapon(ctx, new Weapon { SpreadDegrees = 1f, SpreadPerShot = 1f, MaxSpreadDegrees = 5f, SpreadRecovery = 10f, FireInterval = 0.02f });
        await ctx.PhysicsFrames(1);
        Check.Near(1f, weapon.CurrentSpreadDegrees(), 0.001f, "base spread");
        await HoldTrigger(ctx, weapon, 12, Vector3.Up * 5, Vector3.Up);
        Check.Near(5f, weapon.CurrentSpreadDegrees(), 0.6f, "bloom capped at max");
        Check.Near(2.5f, weapon.CurrentSpreadDegrees(0.5f), 0.4f, "multiplier scales it");
        await ctx.PhysicsFrames(60);
        Check.Near(1f, weapon.CurrentSpreadDegrees(), 0.001f, "recovers to base");
    }

    [Test]
    public async Task ScatterStaysInsideTheConeAndFillsIt(TestContext ctx)
    {
        await ctx.PhysicsFrames(1);
        var forward = new Vector3(0.3f, -0.2f, -1f).Normalized();
        var half = Mathf.DegToRad(4f);
        var angles = Enumerable.Range(0, 4000).Select(_ => forward.AngleTo(Weapon.Scatter(forward, half))).ToList();
        Check.True(angles.Max() <= half + 0.0001f, $"never outside the cone (max {Mathf.RadToDeg(angles.Max()):F2} deg)");
        Check.True(angles.Count(a => a > half * 0.9f) > 400, "reaches the edge (uniform over area: ~19% beyond 0.9)");
        Check.Near(2f / 3f, angles.Average() / half, 0.03f, "mean angle 2/3 of the cone, as uniform-over-area should be");
        Check.Equal(forward, Weapon.Scatter(forward, 0f), "zero spread is exact");
    }

    [Test]
    public async Task PelletsEachDealDamage(TestContext ctx)
    {
        var dummy = AddDummy(ctx, new Vector3(0, 0, -3));
        var shotgun = AddWeapon(ctx, new Weapon { Damage = 5f, Pellets = 8, SpreadDegrees = 3f });
        await ctx.PhysicsFrames(1);
        shotgun.TryFire(null, 0, new Vector3(0, 1, 0), Vector3.Forward);
        Check.Near(40f, dummy.DamageTaken, 0.001f, "8 pellets x 5 at close range");
    }

    [Test]
    public async Task HitSignalReportsDamageAndKills(TestContext ctx)
    {
        var dummy = AddDummy(ctx, new Vector3(0, 0, -5), health: 10f);
        var weapon = AddWeapon(ctx, new Weapon { Damage = 6f, FireInterval = 0.05f });
        var hits = new List<(float damage, bool killed)>();
        weapon.Hit += (_, damage, killed) => hits.Add((damage, killed));
        await ctx.PhysicsFrames(1);
        await HoldTrigger(ctx, weapon, 20, new Vector3(0, 1, 0), Vector3.Forward);
        Check.Equal(2, hits.Count, "two hits (no hits reported on a dead target)");
        Check.True(!hits[0].killed && hits[1].killed, "second hit is the kill");
    }

    [Test]
    public async Task FriendlyTargetsAreNotHurtOrReported(TestContext ctx)
    {
        var friend = AddDummy(ctx, new Vector3(0, 0, -5));
        friend.Team = 0;
        var weapon = AddWeapon(ctx, new Weapon { Damage = 6f });
        var hits = 0;
        weapon.Hit += (_, _, _) => hits++;
        await ctx.PhysicsFrames(1);
        weapon.TryFire(null, 0, new Vector3(0, 1, 0), Vector3.Forward);
        Check.Near(0f, friend.DamageTaken, 0.001f, "no friendly damage");
        Check.Equal(0, hits, "no hit marker on friends");
    }

    [Test]
    public async Task ProjectilesTakeTimeToArrive(TestContext ctx)
    {
        var dummy = AddDummy(ctx, new Vector3(0, 0, -10));
        var weapon = AddWeapon(ctx, new Weapon { Damage = 20f, ProjectileSpeed = 20f, Range = 50f });
        var killedReported = false;
        weapon.Hit += (_, _, _) => killedReported = true;
        await ctx.PhysicsFrames(1);
        weapon.TryFire(null, 0, new Vector3(0, 1, 0), Vector3.Forward);
        await ctx.PhysicsFrames(15); // 0.25 s: ~5 m of 9.5
        Check.Near(0f, dummy.DamageTaken, 0.001f, "not there yet");
        await ctx.PhysicsFrames(25);
        Check.Near(20f, dummy.DamageTaken, 0.001f, "arrived after ~0.5 s");
        Check.True(killedReported, "projectile hits report through the weapon's Hit signal");
    }

    [Test]
    public async Task ProjectilesFall(TestContext ctx)
    {
        var weapon = AddWeapon(ctx, new Weapon { ProjectileSpeed = 20f, ProjectileGravity = 10f, Range = 200f });
        await ctx.PhysicsFrames(1);
        weapon.TryFire(null, 0, new Vector3(0, 50, 0), Vector3.Forward);
        await ctx.PhysicsFrames(60);
        // FindChildren's type filter only knows engine classes, so filter in C#.
        var root = ctx.Tree.CurrentScene ?? ctx.Tree.Root;
        var projectile = root.FindChildren("*", "", true, false).OfType<Projectile>().Single();
        Check.Near(45f, projectile.GlobalPosition.Y, 0.3f, "dropped ~0.5 g t^2 = 5 m in 1 s");
        Check.Near(-20f, projectile.GlobalPosition.Z, 0.5f, "kept its horizontal speed");
        projectile.QueueFree();
    }

    [Test]
    public async Task SplashHurtsLessFurtherOut(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0, navigation: false);
        var near = AddDummy(ctx, new Vector3(1f, 0, -10));
        var far = AddDummy(ctx, new Vector3(-2.5f, 0, -10));
        var outside = AddDummy(ctx, new Vector3(6f, 0, -10));
        var cannon = AddWeapon(ctx, new Weapon { Damage = 100f, ProjectileSpeed = 40f, SplashRadius = 4f, SplashEdgeDamage = 0.2f, Range = 60f });
        await ctx.PhysicsFrames(1);
        // Aim at the ground between the dummies.
        var origin = new Vector3(0, 3, 0);
        cannon.TryFire(null, 0, origin, (new Vector3(0, 0, -10) - origin).Normalized());
        await ctx.PhysicsFrames(40);
        Check.True(near.DamageTaken > far.DamageTaken && far.DamageTaken > 0f, $"falloff: near {near.DamageTaken:F0}, far {far.DamageTaken:F0}");
        Check.Near(0f, outside.DamageTaken, 0.001f, "nothing outside the radius");
    }

    [Test]
    public async Task UnitsTightenSpreadWhenAimingAndLoosenWhenMoving(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0, navigation: false);
        var unit = ctx.Add(Prototype.CreateTrooper("Aimer", 0, Colors.White));
        unit.Weapon!.AdsSpreadMultiplier = 0.4f;
        unit.MovingSpreadMultiplier = 2f;
        unit.AiSpreadMultiplier = 1f;
        var controller = new FixedIntentController(new UnitIntent { AimDownSights = true });
        unit.AiController = controller;
        await ctx.PhysicsFrames(5);
        Check.True(unit.IsAimingDownSights, "intent reaches the unit");
        Check.Near(0.4f, unit.SpreadMultiplier, 0.001f, "aiming tightens");
        controller.Intent = new UnitIntent { Move = Vector3.Forward };
        await ctx.PhysicsFrames(10);
        Check.Near(2f, unit.SpreadMultiplier, 0.001f, "moving loosens");
    }

    [Test]
    public async Task RecoilKicksThePilotsViewAndRecovers(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0, navigation: false);
        var unit = ctx.Add(Prototype.CreateTrooper("Shooter", 0, Colors.White));
        unit.Weapon!.RecoilPitchDegrees = 3f;
        unit.Weapon.RecoilYawDegrees = 0f;
        await ctx.PhysicsFrames(2);
        var pilot = new PilotController();
        unit.BeginPilot(pilot);
        var before = pilot.LookPitch;

        unit.Weapon.TryFire(unit, 0, unit.EyePosition, unit.AimDirection);
        Check.Near(before + Mathf.DegToRad(3f), pilot.LookPitch, 0.0001f, "view kicked up");
        await ctx.PhysicsFrames(90);
        Check.Near(before, pilot.LookPitch, 0.002f, "pitch drifted back down");

        unit.EndPilot();
        var afterRelease = pilot.LookPitch;
        unit.Weapon.TryFire(unit, 0, unit.EyePosition, unit.AimDirection);
        Check.Near(afterRelease, pilot.LookPitch, 0.0001f, "no recoil after the pilot leaves");
    }

    [Test]
    public async Task FollowViewsZoomSmoothly(TestContext ctx)
    {
        await ctx.PhysicsFrames(1);
        var rig = new ChaseRig();
        Check.Near(75f, rig.Fov, 0.001f, "unzoomed");
        rig.Zoom = 2f;
        rig.Evaluate(1.0 / 60.0);
        Check.True(rig.Fov < 75f && rig.Fov > 37.5f, $"eases, doesn't snap ({rig.Fov:F1})");
        for (var i = 0; i < 60; i++) rig.Evaluate(1.0 / 60.0);
        Check.Near(37.5f, rig.Fov, 0.1f, "settles at half the field of view");
        Check.Near(80f, new FirstPersonRig().Fov, 0.001f, "first person keeps its own base FOV");
    }

    [Test]
    public async Task WeaponDefinitionsCarryTheNewFields(TestContext ctx)
    {
        await ctx.PhysicsFrames(1);
        var definition = new WeaponDefinition
        {
            Damage = 10f, FireInterval = 0.1f, Pellets = 2, MagazineSize = 10, ReloadTime = 1f,
            SpreadDegrees = 1.5f, RecoilPitchDegrees = 2f, ProjectileSpeed = 30f, SplashRadius = 2f, AdsZoom = 2f,
        };
        var weapon = ctx.Add(definition.CreateWeapon());
        await ctx.PhysicsFrames(1);
        Check.Equal(10, weapon.Ammo, "magazine filled on ready");
        Check.True(weapon.Pellets == 2 && weapon.SpreadDegrees == 1.5f && weapon.RecoilPitchDegrees == 2f, "accuracy and recoil copied");
        Check.True(weapon.ProjectileSpeed == 30f && weapon.SplashRadius == 2f && weapon.AdsZoom == 2f, "projectile and aiming copied");
        // 2 pellets x 10 dmg x 10 rounds over (10 x 0.1 s firing + 1 s reload) = 100 dps.
        Check.Near(100f, definition.DamagePerSecond, 0.01f, "DPS includes pellets and reload downtime");
    }

    [Test]
    public async Task AiReloadsAndKeepsFighting(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0, navigation: false);
        var unit = ctx.Add(Prototype.CreateTrooper("Gunner", 0, Colors.White));
        unit.Weapon!.MagazineSize = 3;
        unit.Weapon.ReloadTime = 0.3f;
        unit.Weapon.Refill();
        // AI targets units, so the target is a passive enemy unit: 100 hp at 8 damage = 13 hits, 4+ magazines.
        var target = Prototype.CreateTrooper("Target", 1, Colors.Red);
        target.Position = new Vector3(0, 0.1f, -12);
        target.AutoEngageWhenIdle = false;
        ctx.Add(target);
        var reloads = 0;
        unit.Weapon.ReloadStarted += () => reloads++;
        await ctx.Until(() => !target.IsAlive, 60 * 10, "AI to kill the target across several reloads");
        Check.True(reloads >= 3, $"reloaded along the way ({reloads})");
    }

    [Test]
    public async Task PilotAimingZoomsAndHitsShowOnTheHud(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 40f, coverCount: 0, navigation: false);
        var unit = ctx.Add(Prototype.CreateTrooper("Player", 0, Colors.Blue));
        unit.Weapon!.AdsZoom = 2f;
        var enemy = Prototype.CreateTrooper("Enemy", 1, Colors.Red);
        enemy.Position = new Vector3(0, 0.1f, -8);
        enemy.AutoEngageWhenIdle = false;
        ctx.Add(enemy);
        var modes = ctx.Add(new GameModeManager { PlayerTeam = 0, DefaultPilotView = 0 });
        var layer = ctx.Add(new CanvasLayer());
        var hud = new CommandHud();
        layer.AddChild(hud);
        await ctx.PhysicsFrames(3);
        Check.True(modes.TryPilot(unit), "piloting");
        await ctx.PhysicsFrames(30);
        var rig = (FollowRig)modes.Director!.Active;
        var unzoomed = rig.Fov;

        Input.ActionPress(UnitActions.Aim);
        try
        {
            await ctx.PhysicsFrames(40);
            Check.True(modes.Pilot!.IsAiming && unit.IsAimingDownSights, "aim button reaches pilot and unit");
            Check.Near(unzoomed / 2f, rig.Fov, 0.5f, "view zoomed by the weapon's AdsZoom");
        }
        finally
        {
            Input.ActionRelease(UnitActions.Aim);
        }

        unit.Weapon.TryFire(unit, 0, unit.EyePosition, (enemy.AimPoint - unit.EyePosition).Normalized());
        await ctx.PhysicsFrames(1);
        Check.True(hud.HitMarkerVisible && !hud.HitMarkerIsKill, "hit marker shows for the pilot's hit");

        await ctx.PhysicsFrames(40);
        Check.True(!modes.Pilot!.IsAiming, "released aim");
        Check.Near(unzoomed, rig.Fov, 0.5f, "zoom eased back out");
    }
}
