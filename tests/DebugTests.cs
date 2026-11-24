using System;
using System.Text;
using System.Threading.Tasks;
using Dof.Debug;
using Dof.Prototyping;
using Dof.Testing;
using Dof.UnitDebug;
using Dof.Units;
using Godot;

namespace Dof.Tests;

public class DebugTests
{
    private static async Task Frames(TestContext ctx, int count)
    {
        for (var i = 0; i < count; i++) await ctx.World.ToSignal(ctx.Tree, SceneTree.SignalName.ProcessFrame);
    }

    private sealed class CountingSection : IDebugSection
    {
        public string Title => "Counter";
        public DebugToggle Toggle { get; private set; } = null!;
        public int Draws;

        public void Attach(DebugOverlay overlay) => Toggle = overlay.AddToggle("Count", on: false);
        public void Write(StringBuilder text) => text.AppendLine($"draws so far {Draws}");
        public void Draw(double delta) => Draws++;
    }

    private sealed class BrokenSection : IDebugSection
    {
        public string Title => "Broken";
        public void Write(StringBuilder text) => text.AppendLine("still here");
        public void Draw(double delta) => throw new InvalidOperationException("deliberate test failure");
    }

    [Test]
    public async Task DrawingDoesNothingWithoutAnOpenOverlay(TestContext ctx)
    {
        var overlay = ctx.Add(new DebugOverlay());
        await Frames(ctx, 2);
        Check.True(!overlay.IsOpen && !DebugDraw.Enabled, "starts closed");
        DebugDraw.Line(Vector3.Zero, Vector3.Up, Colors.Red);
        DebugDraw.Circle(Vector3.Zero, 2f, Colors.Red);
        DebugDraw.Text(Vector3.Zero, "hidden");
        Check.Equal(0, DebugDraw.PendingLines + DebugDraw.PendingTexts, "nothing is queued while closed");
    }

    [Test]
    public async Task ToggleActionOpensAndClosesTheOverlay(TestContext ctx)
    {
        var overlay = ctx.Add(new DebugOverlay());
        await Frames(ctx, 1);
        Input.ParseInputEvent(new InputEventAction { Action = DebugActions.Toggle, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = DebugActions.Toggle, Pressed = false });
        await Frames(ctx, 2);
        Check.True(overlay.IsOpen && DebugDraw.Enabled && DebugOverlay.Current == overlay, "F3 action opens it");

        DebugDraw.Line(Vector3.Zero, Vector3.Up, Colors.Red, seconds: 10f);
        Input.ParseInputEvent(new InputEventAction { Action = DebugActions.Toggle, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = DebugActions.Toggle, Pressed = false });
        await Frames(ctx, 2);
        Check.True(!overlay.IsOpen && !DebugDraw.Enabled, "pressing it again closes it");
        Check.Equal(0, DebugDraw.PendingLines, "closing drops kept shapes");
    }

    [Test]
    public async Task ShapesLastOneFrameUnlessKept(TestContext ctx)
    {
        var overlay = ctx.Add(new DebugOverlay { StartOpen = true, ShowPerformance = false });
        await Frames(ctx, 1);

        DebugDraw.Line(Vector3.Zero, Vector3.Up, Colors.Red);
        DebugDraw.Line(Vector3.Zero, Vector3.Right, Colors.Red, seconds: 0.5f);
        DebugDraw.Circle(Vector3.Zero, 1f, Colors.Green, segments: 8);
        await Frames(ctx, 1);
        Check.Equal(10, overlay.RenderedLines, "one line, one kept line and an 8-segment circle");

        await Frames(ctx, 2);
        Check.Equal(1, overlay.RenderedLines, "one-frame shapes are gone, the kept one stays");

        await Frames(ctx, 40);
        Check.Equal(0, overlay.RenderedLines, "the kept line expires after its time");
    }

    [Test]
    public async Task FreeingTheOverlayStopsDrawing(TestContext ctx)
    {
        var overlay = ctx.Add(new DebugOverlay { StartOpen = true });
        await Frames(ctx, 1);
        DebugDraw.Line(Vector3.Zero, Vector3.Up, Colors.Red, seconds: 10f);
        overlay.QueueFree();
        await Frames(ctx, 1);
        Check.True(!DebugDraw.Enabled && DebugOverlay.Current is null, "drawing turns off with the overlay");
        Check.Equal(0, DebugDraw.PendingLines, "and its shapes are dropped");
    }

    [Test]
    public async Task GamesAddSectionsAndToggles(TestContext ctx)
    {
        var overlay = new DebugOverlay { StartOpen = true };
        var counter = new CountingSection();
        overlay.AddSection(counter); // before it enters the tree
        ctx.Add(overlay);
        await Frames(ctx, 5);

        Check.True(counter.Draws >= 4, $"Draw runs every frame while open ({counter.Draws})");
        overlay.RefreshPanel();
        Check.True(overlay.PanelText.Contains("Performance") && overlay.PanelText.Contains("fps"), "built-in performance section");
        Check.True(overlay.PanelText.Contains("— Counter —") && overlay.PanelText.Contains("draws so far"), $"custom section text:\n{overlay.PanelText}");

        Check.True(overlay.AddToggle("Count") == counter.Toggle, "asking for a toggle by name returns the same one");
        Check.True(counter.Toggle.Box is not null && !counter.Toggle.Box.ButtonPressed, "toggle has a checkbox, off as asked");
        counter.Toggle.On = true;
        Check.True(counter.Toggle.Box!.ButtonPressed, "setting a toggle in code updates its checkbox");
        counter.Toggle.Box.ButtonPressed = false;
        Check.True(!counter.Toggle.On, "clicking the checkbox updates the toggle");

        overlay.SetOpen(false);
        var draws = counter.Draws;
        await Frames(ctx, 3);
        Check.Equal(draws, counter.Draws, "sections don't run while closed");
    }

    [Test]
    public async Task AFailingSectionIsSwitchedOff(TestContext ctx)
    {
        IDebugSection? reported = null;
        var reports = 0;
        // Captured instead of logged: CI fails on any engine error in the log.
        var overlay = new DebugOverlay { StartOpen = true, SectionFailed = (section, _) => { reported = section; reports++; } };
        var counter = new CountingSection();
        overlay.AddSection(new BrokenSection());
        overlay.AddSection(counter);
        ctx.Add(overlay);
        await Frames(ctx, 3);
        overlay.RefreshPanel();
        Check.True(!overlay.PanelText.Contains("Broken"), "the throwing section is dropped from the panel");
        Check.True(counter.Draws >= 2 && overlay.PanelText.Contains("Counter"), "other sections keep running");
        Check.True(reported is BrokenSection && reports == 1, $"the failure is reported once ({reports})");
    }

    [Test]
    public async Task TextIsPlacedOnScreenAndSkippedBehindTheCamera(TestContext ctx)
    {
        var camera = ctx.Add(new Camera3D { Current = true });
        await ctx.PhysicsFrames(1);
        var bounds = new Rect2(Vector2.Zero, ctx.World.GetViewport().GetVisibleRect().Size);
        Check.True(DebugOverlay.TryProject(camera, new Vector3(0, 0, -5), bounds, out var centre), "point ahead is on screen");
        Check.True(centre.DistanceTo(bounds.Size / 2f) < 2f, $"point straight ahead is mid-screen, got {centre}");
        Check.True(!DebugOverlay.TryProject(camera, new Vector3(0, 0, 5), bounds, out _), "point behind is skipped");
        Check.True(!DebugOverlay.TryProject(camera, new Vector3(500, 0, -5), bounds, out _), "point far off to the side is skipped");
    }

    [Test]
    public async Task CurrentEnemyNeverTriggersAScan(TestContext ctx)
    {
        var unit = Prototype.CreateTrooper("Watcher", 0, Colors.Blue);
        unit.AutoEngageWhenIdle = false;
        ctx.Add(unit);
        var enemy = Prototype.CreateTrooper("Enemy", 1, Colors.Red);
        enemy.Position = new Vector3(0, 0.1f, -6);
        enemy.AutoEngageWhenIdle = false;
        ctx.Add(enemy);
        await ctx.PhysicsFrames(30);

        Check.True(unit.CurrentEnemy is null, "asking doesn't scan, so nothing is known yet");
        Check.True(unit.NearestEnemyInRange() == enemy, "a real scan finds the enemy");
        Check.True(unit.CurrentEnemy == enemy, "and then it is reported");
        enemy.Health.TakeDamage(1000f);
        Check.True(unit.CurrentEnemy is null, "a dead enemy isn't reported");
    }

    [Test]
    public async Task UnitViewDrawsPathsTargetsAndLabels(TestContext ctx)
    {
        var baker = Prototype.BuildBattlefield(ctx.World, size: 60f, coverCount: 0);
        if (baker is not null) await ctx.World.ToSignal(baker, Dof.Navigation.NavmeshBaker.SignalName.Baked);

        var walker = Prototype.CreateTrooper("Walker", 0, Colors.Blue);
        walker.Position = new Vector3(-4, 0.1f, 0);
        ctx.Add(walker);
        var guard = Prototype.CreateTrooper("Guard", 1, Colors.Red);
        guard.Position = new Vector3(4, 0.1f, -4);
        ctx.Add(guard);
        var camera = ctx.Add(new Camera3D { Current = true, Position = new Vector3(0, 20, 14) });
        camera.LookAt(Vector3.Zero);

        var overlay = new DebugOverlay { StartOpen = true, ShowPerformance = false };
        var view = new UnitDebugSection();
        overlay.AddSection(view);
        ctx.Add(overlay);
        await ctx.PhysicsFrames(2);
        walker.Health.Invulnerable = true;
        guard.Health.Invulnerable = true;
        walker.Issue(new MoveCommand(new Vector3(-4, 0, -20)));
        walker.Issue(new MoveCommand(new Vector3(10, 0, -20)), append: true);

        await ctx.PhysicsFrames(30);
        await Frames(ctx, 1);
        Check.True(view.Paths.On && view.Orders.On && view.Targets.On && view.Labels.On, "main layers start on");
        Check.True(!view.Ranges.On && !view.Avoidance.On, "busy layers start off");
        Check.True(walker.Path.UsingNavmesh, "walker is on a navmesh path");
        Check.True(guard.CurrentEnemy == walker, "guard has the walker as its target");
        Check.Equal(2, view.LabelsDrawn, "a label over each unit");
        var withMainLayers = overlay.RenderedLines;
        Check.True(withMainLayers >= 1 + 3 + 3 + 2 + 1, $"path, two order crosses, an arrow to the second order and a target line ({withMainLayers} lines)");

        overlay.RefreshPanel();
        Check.True(overlay.PanelText.Contains("team 0: 1 alive   1 ordered"), $"team 0 counts:\n{overlay.PanelText}");
        Check.True(overlay.PanelText.Contains("team 1: 1 alive   0 ordered   1 engaging"), "team 1 counts");

        view.Ranges.On = true;
        await Frames(ctx, 2);
        Check.True(overlay.RenderedLines >= withMainLayers + 4 * 48 - 10, $"ranges add two 48-segment circles per unit ({overlay.RenderedLines})");

        foreach (var toggle in overlay.Toggles) toggle.On = false;
        await Frames(ctx, 2);
        Check.Equal(0, overlay.RenderedLines, "everything off draws nothing");
        Check.Equal(0, view.LabelsDrawn, "and no labels");
    }

    [Test]
    public async Task LabelsAreCappedToTheNearest(TestContext ctx)
    {
        for (var i = 0; i < 6; i++)
        {
            var unit = Prototype.CreateTrooper($"U{i}", 0, Colors.White);
            unit.Position = new Vector3(0, 0.1f, -4 - i * 3);
            ctx.Add(unit);
        }
        ctx.Add(new Camera3D { Current = true, Position = new Vector3(0, 2, 0) });
        var overlay = new DebugOverlay { StartOpen = true, ShowPerformance = false };
        var view = new UnitDebugSection { MaxLabels = 3, LabelDistance = 100f };
        overlay.AddSection(view);
        ctx.Add(overlay);
        await ctx.PhysicsFrames(2);
        await Frames(ctx, 1);
        Check.Equal(3, view.LabelsDrawn, "only MaxLabels labels");
        Check.Equal(3, overlay.RenderedTexts, "and only those reach the screen");

        view.LabelDistance = 9f;
        await Frames(ctx, 2);
        Check.Equal(2, view.LabelsDrawn, "units beyond LabelDistance get none");
    }
}
