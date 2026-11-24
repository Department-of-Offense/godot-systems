using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Dof.Testing;

/// <summary>Marks a test method: <c>public async Task Name(TestContext ctx)</c> on a class with a parameterless constructor.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public sealed class TestFailure(string message) : Exception(message);

/// <summary>A fresh 3D world per test, plus helpers for stepping the simulation.</summary>
public sealed class TestContext(Node3D world)
{
    public Node3D World { get; } = world;
    public SceneTree Tree => World.GetTree();

    public async Task PhysicsFrames(int count)
    {
        for (var i = 0; i < count; i++)
            await World.ToSignal(Tree, SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>Step physics until <paramref name="condition"/> holds; fail after <paramref name="maxFrames"/>.</summary>
    public async Task Until(Func<bool> condition, int maxFrames, string what)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (condition()) return;
            await World.ToSignal(Tree, SceneTree.SignalName.PhysicsFrame);
        }
        if (!condition()) throw new TestFailure($"Timed out after {maxFrames} physics frames waiting for: {what}");
    }

    public T Add<T>(T node) where T : Node
    {
        World.AddChild(node);
        return node;
    }
}

public static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestFailure($"{what}: expected {expected}, got {actual}");
    }

    public static void Near(float expected, float actual, float tolerance, string what)
    {
        if (Mathf.Abs(expected - actual) > tolerance)
            throw new TestFailure($"{what}: expected {expected} ± {tolerance}, got {actual}");
    }
}

/// <summary>
/// Headless test runner. Finds every [Test] method in the project's assembly,
/// runs each in its own world, prints results and exits with code 1 on any
/// failure. Make it the root of a scene and run:
/// <c>godot --headless --fixed-fps 60 --path . res://tests/TestRunner.tscn -- --filter=Name</c>
/// </summary>
[GlobalClass]
public partial class TestRunner : Node
{
    public override void _Ready() => _ = RunAll();

    private async Task RunAll()
    {
        var filter = OS.GetCmdlineUserArgs()
            .FirstOrDefault(a => a.StartsWith("--filter="))?["--filter=".Length..];

        var tests = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
                .Select(m => (Type: t, Method: m)))
            .Where(x => filter is null || $"{x.Type.Name}.{x.Method.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Type.Name).ThenBy(x => x.Method.Name)
            .ToList();

        GD.Print($"Running {tests.Count} test(s)");
        var failures = new List<string>();
        var clock = Stopwatch.StartNew();

        foreach (var (type, method) in tests)
        {
            var name = $"{type.Name}.{method.Name}";
            var world = new Node3D { Name = "TestWorld" };
            AddChild(world);
            try
            {
                var instance = Activator.CreateInstance(type);
                if (method.Invoke(instance, new object[] { new TestContext(world) }) is Task task) await task;
                GD.Print($"  PASS  {name}");
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
                var message = inner is TestFailure ? inner.Message : inner.ToString();
                failures.Add($"{name}: {message}");
                GD.PrintErr($"  FAIL  {name}: {message}");
            }

            world.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        GD.Print($"{tests.Count - failures.Count} passed, {failures.Count} failed ({clock.Elapsed.TotalSeconds:0.0}s)");
        GetTree().Quit(failures.Count == 0 && tests.Count > 0 ? 0 : 1);
    }
}
