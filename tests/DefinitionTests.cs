using System.Linq;
using System.Threading.Tasks;
using Dof.Combat;
using Dof.Prototyping;
using Dof.Testing;
using Dof.Units;
using Godot;

namespace Dof.Tests;

/// <summary>A game-side subclass adding its own fields, as games are expected to.</summary>
public partial class CostedUnitDefinition : UnitDefinition
{
    [Export] public int Cost { get; set; }
    [Export] public bool Invulnerable { get; set; }

    public override void Configure(Unit unit)
    {
        base.Configure(unit);
        unit.SetMeta("cost", Cost);
        unit.GetChildren().OfType<Health>().Single().Invulnerable = Invulnerable;
    }
}

public class DefinitionTests
{
    private static UnitDefinition Scout() => new()
    {
        Id = "test_scout",
        DisplayName = "Test Scout",
        MaxHealth = 60f,
        MoveSpeed = 9f,
        Radius = 0.3f,
        Height = 1.6f,
        EngageRangeFactor = 0.5f,
        Weapon = new WeaponDefinition { Damage = 4f, FireInterval = 0.1f, Range = 20f },
    };

    [Test]
    public async Task SpawnedUnitsTakeTheDefinitionsStats(TestContext ctx)
    {
        var unit = ctx.Add(Scout().Spawn(0, Colors.Blue));
        await ctx.PhysicsFrames(1);
        Check.Equal("Test Scout", unit.DisplayName, "display name");
        Check.Near(9f, unit.MoveSpeed, 0.001f, "move speed");
        Check.Near(60f, unit.Health.MaxHealth, 0.001f, "max health");
        Check.Near(60f, unit.Health.Current, 0.001f, "starts at full health");
        Check.True(unit.Weapon is not null, "weapon added");
        Check.Near(4f, unit.Weapon!.Damage, 0.001f, "weapon damage");
        Check.Near(10f, unit.EngageRange, 0.001f, "engage range = weapon range x factor");
        var capsule = unit.GetChildren().OfType<CollisionShape3D>().Single().Shape as CapsuleShape3D;
        Check.Near(0.3f, capsule!.Radius, 0.001f, "collider radius");
        Check.True(unit.GetNodeOrNull("Model") is not null, "placeholder model added");
    }

    [Test]
    public async Task UnitsDoNotShareStateWithTheDefinition(TestContext ctx)
    {
        var type = Scout();
        var a = ctx.Add(type.Spawn(0));
        var b = ctx.Add(type.Spawn(0));
        await ctx.PhysicsFrames(1);
        a.Weapon!.Damage = 100f; // an upgrade on one unit
        a.MoveSpeed = 1f;
        Check.Near(4f, b.Weapon!.Damage, 0.001f, "other unit's weapon unchanged");
        Check.Near(4f, type.Weapon!.Damage, 0.001f, "definition unchanged");
        Check.Near(9f, b.MoveSpeed, 0.001f, "other unit's speed unchanged");
    }

    [Test]
    public async Task SceneUnitsCanPointAtADefinition(TestContext ctx)
    {
        // Like a Unit placed in the editor with only Definition set.
        var unit = ctx.Add(new Unit { Definition = Scout(), Team = 1 });
        await ctx.PhysicsFrames(1);
        Check.Near(9f, unit.MoveSpeed, 0.001f, "stats applied on ready");
        Check.True(unit.Weapon is not null && unit.Health.MaxHealth == 60f, "parts added on ready");
    }

    [Test]
    public async Task HandBuiltPartsAreKeptAndRetuned(TestContext ctx)
    {
        var unit = new Unit { Definition = Scout() };
        var ownShape = new CollisionShape3D { Shape = new BoxShape3D() };
        var ownWeapon = new Weapon { Name = "Gun", Damage = 1f };
        unit.AddChild(ownShape);
        unit.AddChild(ownWeapon);
        ctx.Add(unit);
        await ctx.PhysicsFrames(1);
        Check.Equal(1, unit.GetChildren().OfType<CollisionShape3D>().Count(), "no second collider");
        Check.True(unit.Weapon == ownWeapon, "kept the scene's own weapon");
        Check.Near(4f, ownWeapon.Damage, 0.001f, "but tuned it to the definition");
    }

    [Test]
    public async Task ModelScenesAreUsedAndTeamColoured(TestContext ctx)
    {
        var model = new Node3D { Name = "Mech" };
        var hull = new MeshInstance3D { Name = "Hull", Mesh = new BoxMesh() };
        var trim = new MeshInstance3D { Name = "Trim", Mesh = new BoxMesh() };
        model.AddChild(hull);
        hull.Owner = model;
        model.AddChild(trim);
        trim.Owner = model;
        hull.AddToGroup(UnitDefinition.TeamColorGroup, persistent: true);
        var scene = new PackedScene();
        Check.Equal(Error.Ok, scene.Pack(model), "pack model scene");
        model.Free();

        var type = Scout();
        type.Model = scene;
        var unit = ctx.Add(type.Spawn(1, Colors.Red));
        await ctx.PhysicsFrames(1);
        var instanced = unit.GetNode("Model");
        Check.True(instanced.GetNodeOrNull("Hull") is not null, "model scene instanced");
        var hullMaterial = instanced.GetNode<MeshInstance3D>("Hull").MaterialOverride as StandardMaterial3D;
        Check.Equal(Colors.Red, hullMaterial?.AlbedoColor ?? Colors.Black, "team-colour group tinted");
        Check.True(instanced.GetNode<MeshInstance3D>("Trim").MaterialOverride is null, "other meshes left alone");
    }

    [Test]
    public async Task GamesCanSubclassDefinitions(TestContext ctx)
    {
        var type = new CostedUnitDefinition { Id = "turret", Cost = 150, Invulnerable = true, MoveSpeed = 0f };
        var unit = ctx.Add(type.Spawn(0));
        await ctx.PhysicsFrames(1);
        Check.Equal(150, unit.GetMeta("cost").AsInt32(), "custom field applied");
        unit.Health.TakeDamage(1000f);
        Check.True(unit.IsAlive, "custom Configure ran after base (invulnerable)");
    }

    [Test]
    public async Task CatalogLoadsDefinitionFiles(TestContext ctx)
    {
        await ctx.PhysicsFrames(1);
        var catalog = UnitCatalog.LoadFolder("res://demo/units");
        Check.Equal(3, catalog.Count, "three unit types (weapon files skipped)");
        var heavy = catalog.Get("heavy");
        Check.Near(220f, heavy.MaxHealth, 0.001f, "heavy health from .tres");
        Check.Equal("Cannon", heavy.Weapon?.DisplayName ?? "", "heavy's weapon from its own .tres");
        Check.Equal(3, catalog.WithTag("infantry").Count(), "tag query");
        Check.Equal("scout", catalog.WithTag("fast").Single().Id, "single-tag query");
        Check.True(!catalog.Add(new UnitDefinition { Id = "heavy" }), "duplicate ids rejected");
    }

    [Test]
    public async Task UnitsFromFilesFight(TestContext ctx)
    {
        Prototype.BuildBattlefield(ctx.World, size: 60f, coverCount: 0, navigation: false);
        var catalog = UnitCatalog.LoadFolder("res://demo/units");
        var heavy = Prototype.CreateUnit(catalog.Get("heavy"), "Heavy", 0, Colors.Blue);
        var scout = Prototype.CreateUnit(catalog.Get("scout"), "Scout", 1, Colors.Red);
        heavy.Position = new Vector3(0, 0.1f, 0);
        scout.Position = new Vector3(0, 0.1f, -15);
        scout.AutoEngageWhenIdle = false; // a passive target, so the result is about the heavy
        ctx.Add(heavy);
        ctx.Add(scout);
        heavy.Issue(new AttackCommand(scout));
        await ctx.Until(() => !scout.IsAlive, 60 * 10, "heavy to kill the scout (60 hp, 30 per cannon hit)");
        Check.True(heavy.Health.Current > 200f, "scout never fired back");
    }
}
