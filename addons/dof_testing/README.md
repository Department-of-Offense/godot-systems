# Testing (`dof_testing`)

Headless test runner: [Test] methods, a fresh 3D world per test, simulated time.

**Depends on:** nothing

Make a scene whose root runs `TestRunner.cs`, then run `godot --headless --fixed-fps 60 --path . res://tests/TestRunner.tscn`. Filter with `-- --filter=Name`. Tests are `public async Task Name(TestContext ctx)` marked `[Test]`.
