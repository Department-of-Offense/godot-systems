using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dof.Cameras;
using Dof.Combat;
using Dof.Testing;
using Godot;

namespace Dof.Tests;

/// <summary>
/// Enforces the dependency rules between systems. Godot compiles every C#
/// file into one assembly, so the compiler can't stop dof_cameras from
/// importing dof_units. This test does: a module may only import namespaces
/// of modules listed in its module.cfg "depends".
/// </summary>
public class DependencyRuleTests
{
    private static readonly Regex UsingDof = new(@"^\s*using\s+(?:static\s+)?Dof\.(\w+)", RegexOptions.Multiline);
    private static readonly Regex QualifiedDof = new(@"\bDof\.(\w+)\.\w+");

    public static Dictionary<string, HashSet<string>> ReadModules()
    {
        var addons = ProjectSettings.GlobalizePath("res://addons");
        var modules = new Dictionary<string, HashSet<string>>();
        foreach (var dir in Directory.GetDirectories(addons, "dof_*"))
        {
            var cfg = new ConfigFile();
            var cfgPath = Path.Combine(dir, "module.cfg");
            if (cfg.Load(cfgPath) != Error.Ok) throw new TestFailure($"{Path.GetFileName(dir)} has no readable module.cfg");
            var depends = ((string)cfg.GetValue("module", "depends", "")).Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            modules[Path.GetFileName(dir)] = depends.ToHashSet();
        }
        return modules;
    }

    [Test]
    public Task ModulesOnlyImportDeclaredDependencies(TestContext ctx)
    {
        var modules = ReadModules();
        Check.True(modules.Count >= 9, $"found {modules.Count} modules");
        var addons = ProjectSettings.GlobalizePath("res://addons");
        var violations = new List<string>();

        foreach (var (module, depends) in modules)
        {
            foreach (var dep in depends)
                if (!modules.ContainsKey(dep)) violations.Add($"{module} depends on unknown module {dep}");

            foreach (var file in Directory.GetFiles(Path.Combine(addons, module), "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file);
                var referenced = UsingDof.Matches(source).Concat(QualifiedDof.Matches(source))
                    .Select(m => "dof_" + m.Groups[1].Value.ToLowerInvariant())
                    .Distinct();
                foreach (var target in referenced)
                    if (target != module && !depends.Contains(target))
                        violations.Add($"{module}/{Path.GetFileName(file)} uses {target}, which is not in its module.cfg depends");
            }
        }

        Check.True(violations.Count == 0, "dependency rule broken:\n    " + string.Join("\n    ", violations));
        return Task.CompletedTask;
    }

    [Test]
    public Task DependencyGraphHasNoCycles(TestContext ctx)
    {
        var modules = ReadModules();
        var state = new Dictionary<string, int>(); // 1 visiting, 2 done
        void Visit(string m, Stack<string> path)
        {
            if (state.GetValueOrDefault(m) == 2) return;
            path.Push(m);
            if (state.GetValueOrDefault(m) == 1)
                throw new TestFailure("dependency cycle: " + string.Join(" -> ", path.Reverse()));
            state[m] = 1;
            foreach (var dep in modules.GetValueOrDefault(m) ?? new HashSet<string>()) Visit(dep, path);
            state[m] = 2;
            path.Pop();
        }
        foreach (var m in modules.Keys) Visit(m, new Stack<string>());
        return Task.CompletedTask;
    }
}

/// <summary>Systems must be usable without the rest of the kit.</summary>
public partial class StandaloneUseTests
{
    /// <summary>A camera subject that is not a unit: e.g. a drone or a spectator.</summary>
    private sealed class Drone(Node3D body) : ICameraSubject
    {
        public Node3D Body => body;
        public float EyeHeight => 0.5f;
        public float LookYaw { get; set; }
        public float LookPitch { get; set; }
    }

    [Test]
    public async Task CamerasFollowAnyNode(TestContext ctx)
    {
        var body = ctx.Add(new Node3D { Position = new Vector3(5, 3, -2) });
        var director = ctx.Add(new CameraDirector());
        await ctx.PhysicsFrames(1);

        var rig = director.Follow(new Drone(body), 0);
        Check.True(rig is FirstPersonRig, "first follow view is first person");
        director.TransitionSeconds = 0;
        await ctx.PhysicsFrames(2);
        await ctx.World.ToSignal(ctx.Tree, SceneTree.SignalName.ProcessFrame);
        var expected = body.GlobalPosition + Vector3.Up * 0.5f;
        Check.True(director.Camera!.GlobalPosition.DistanceTo(expected) < 0.01f,
            $"camera at the drone's eye, got {director.Camera.GlobalPosition}");

        director.FollowViews.Add(new ChaseRig { Distance = 10f });
        Check.Equal(4, director.FollowViews.Count, "games can add follow views");
    }

    [Test]
    public async Task DirectedCamerasAreNotPhysicsInterpolated(TestContext ctx)
    {
        // The director moves cameras every rendered frame; interpolating them too
        // double-smooths and logs "Interpolated Camera3D triggered from outside
        // physics process". Covers both the director's own camera and one a game supplies.
        var own = ctx.Add(new CameraDirector());
        var supplied = new Camera3D();
        ctx.Add(supplied);
        var withSupplied = ctx.Add(new CameraDirector { Camera = supplied });
        await ctx.PhysicsFrames(1);
        Check.True(own.Camera!.PhysicsInterpolationMode == Node.PhysicsInterpolationModeEnum.Off, "director's own camera");
        Check.True(supplied.PhysicsInterpolationMode == Node.PhysicsInterpolationModeEnum.Off, "game-supplied camera");
        Check.True(!own.Camera.IsPhysicsInterpolatedAndEnabled(), "not interpolated even with project-wide interpolation on");
    }

    /// <summary>Something destructible that is not a unit.</summary>
    private partial class Crate : StaticBody3D, ICombatant
    {
        public int Team => Teams.Neutral;
        public Health Health { get; private set; } = null!;

        public override void _Ready()
        {
            AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
            Health = new Health { MaxHealth = 25f };
            AddChild(Health);
        }
    }

    [Test]
    public async Task WeaponsDamageAnyCombatant(TestContext ctx)
    {
        var crate = ctx.Add(new Crate { Position = new Vector3(0, 0, -10) });
        var gun = ctx.Add(new Weapon { Damage = 10f, FireInterval = 0f, ShowTracers = false });
        await ctx.PhysicsFrames(2);

        for (var i = 0; i < 3; i++)
        {
            Check.True(gun.TryFire(null, team: 0, origin: Vector3.Zero, direction: Vector3.Forward), $"shot {i} fired");
            await ctx.PhysicsFrames(1);
        }
        Check.True(!crate.Health.IsAlive, $"crate destroyed, health {crate.Health.Current}");
    }
}
