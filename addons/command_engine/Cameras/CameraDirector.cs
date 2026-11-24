using System;
using System.Collections.Generic;
using CommandEngine.Controllers;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.Cameras;

/// <summary>
/// Owns the single gameplay Camera3D and moves it according to the active
/// rig, blending smoothly whenever the rig changes (strategy to pilot, or
/// between pilot views).
/// </summary>
[GlobalClass]
public partial class CameraDirector : Node3D
{
    [Signal] public delegate void RigChangedEventHandler(string rigName);

    [Export] public Camera3D? Camera { get; set; }
    [Export] public double TransitionSeconds { get; set; } = 0.45;

    public StrategyCameraRig Strategy { get; } = new();
    public FirstPersonRig FirstPerson { get; } = new();
    public ChaseRig Chase { get; } = new();
    public OrbitRig Orbit { get; } = new();

    /// <summary>Pilot views in the order the cycle key steps through them.</summary>
    public IReadOnlyList<PilotRig> PilotViews { get; }

    public ICameraRig Active { get; private set; }
    public int PilotViewIndex { get; private set; }

    private Transform3D _blendFrom;
    private float _blendFromFov;
    private double _blend = 1.0;
    private Unit? _hiddenUnit;

    public CameraDirector()
    {
        PilotViews = new PilotRig[] { FirstPerson, Chase, Orbit };
        Active = Strategy;
    }

    public override void _Ready()
    {
        if (Camera is null)
        {
            Camera = new Camera3D { Name = "GameplayCamera" };
            AddChild(Camera);
        }
        Camera.Current = true;
        Camera.Fov = Active.Fov;
        Camera.GlobalTransform = Active.Evaluate(0);
    }

    public override void _Process(double delta)
    {
        if (Camera is null) return;
        var target = Active.Evaluate(delta);

        if (_blend < 1.0)
        {
            _blend = Math.Min(1.0, _blend + delta / Math.Max(0.0001, TransitionSeconds));
            var t = (float)(_blend * _blend * (3.0 - 2.0 * _blend)); // smoothstep
            Camera.GlobalTransform = _blendFrom.InterpolateWith(target, t);
            Camera.Fov = Mathf.Lerp(_blendFromFov, Active.Fov, t);
        }
        else
        {
            Camera.GlobalTransform = target;
            Camera.Fov = Active.Fov;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Active is PilotRig rig) rig.UpdateCollision(GetWorld3D().DirectSpaceState, delta);
    }

    public override void _UnhandledInput(InputEvent @event) => Active.HandleInput(@event);

    public void ShowStrategy(Vector3? focus = null, float? yaw = null)
    {
        if (focus.HasValue) Strategy.Focus = new Vector3(focus.Value.X, 0f, focus.Value.Z);
        if (yaw.HasValue) Strategy.Yaw = yaw.Value;
        Strategy.AcceptsInput = true;
        RestoreHiddenUnit();
        SetRig(Strategy);
    }

    public void ShowPilotView(Unit unit, PilotController pilot, int index)
    {
        PilotViewIndex = ((index % PilotViews.Count) + PilotViews.Count) % PilotViews.Count;
        var rig = PilotViews[PilotViewIndex];
        rig.Bind(unit, pilot);
        pilot.Camera = Camera;
        pilot.BodyFollowsLook = rig.BodyFollowsLook;
        Strategy.AcceptsInput = false;

        RestoreHiddenUnit();
        if (rig.HidesBody)
        {
            unit.SetFirstPersonHidden(true);
            _hiddenUnit = unit;
        }
        SetRig(rig);
    }

    public void SetRig(ICameraRig rig, bool instant = false)
    {
        if (Camera is not null)
        {
            _blendFrom = Camera.GlobalTransform;
            _blendFromFov = Camera.Fov;
        }
        Active = rig;
        _blend = instant ? 1.0 : 0.0;
        EmitSignal(SignalName.RigChanged, rig.Name);
    }

    private void RestoreHiddenUnit()
    {
        if (_hiddenUnit is not null && IsInstanceValid(_hiddenUnit)) _hiddenUnit.SetFirstPersonHidden(false);
        _hiddenUnit = null;
    }
}
