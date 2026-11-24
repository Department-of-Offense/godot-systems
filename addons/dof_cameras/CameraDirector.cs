using System;
using System.Collections.Generic;
using Godot;

namespace Dof.Cameras;

/// <summary>
/// Owns one Camera3D and moves it according to the active rig, blending
/// smoothly whenever the rig changes. Works on its own: drop it in a scene,
/// call <see cref="ShowStrategy"/> or <see cref="Follow"/>.
///
/// The follow views (first person, chase, orbit by default) are a plain
/// list: add, remove or reorder rigs to suit the game.
/// </summary>
[GlobalClass]
public partial class CameraDirector : Node3D
{
    [Signal] public delegate void RigChangedEventHandler(string rigName);

    /// <summary>
    /// Camera to drive. Created automatically if left empty. The director
    /// moves it every rendered frame (rigs already follow their subjects'
    /// interpolated positions), so its physics interpolation is turned off.
    /// </summary>
    [Export] public Camera3D? Camera { get; set; }
    [Export] public double TransitionSeconds { get; set; } = 0.45;

    public StrategyCameraRig Strategy { get; } = new();

    /// <summary>Views <see cref="CycleFollowView"/> steps through. Edit freely.</summary>
    public List<FollowRig> FollowViews { get; } = new() { new FirstPersonRig(), new ChaseRig(), new OrbitRig() };

    public ICameraRig Active { get; private set; }
    public int FollowViewIndex { get; private set; }
    public ICameraSubject? Subject { get; private set; }

    private Transform3D _blendFrom;
    private float _blendFromFov;
    private double _blend = 1.0;
    private ICameraSubject? _hiddenSubject;

    public CameraDirector() => Active = Strategy;

    public T? FollowView<T>() where T : FollowRig => FollowViews.Find(r => r is T) as T;

    public override void _Ready()
    {
        CameraActions.EnsureDefaults();
        if (Camera is null)
        {
            Camera = new Camera3D { Name = "Camera" };
            AddChild(Camera);
        }
        // Moved in _Process, so it must not be physics-interpolated: that would
        // smooth it a second time and makes Godot warn ("Interpolated Camera3D
        // triggered from outside physics process").
        Camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
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

    public override void _PhysicsProcess(double delta) => Active.PhysicsUpdate(GetWorld3D().DirectSpaceState, delta);

    public override void _UnhandledInput(InputEvent @event) => Active.HandleInput(@event);

    public void ShowStrategy(Vector3? focus = null, float? yaw = null, bool instant = false)
    {
        if (focus.HasValue) Strategy.Focus = new Vector3(focus.Value.X, 0f, focus.Value.Z);
        if (yaw.HasValue) Strategy.Yaw = yaw.Value;
        Strategy.AcceptsInput = true;
        Subject = null;
        RestoreHiddenSubject();
        SetRig(Strategy, instant);
    }

    /// <summary>Follow <paramref name="subject"/> with the follow view at <paramref name="viewIndex"/> (wraps).</summary>
    public FollowRig Follow(ICameraSubject subject, int viewIndex)
    {
        if (FollowViews.Count == 0) throw new InvalidOperationException("CameraDirector has no follow views.");
        FollowViewIndex = ((viewIndex % FollowViews.Count) + FollowViews.Count) % FollowViews.Count;
        var rig = FollowViews[FollowViewIndex];
        rig.Follow(subject);
        Subject = subject;
        Strategy.AcceptsInput = false;

        RestoreHiddenSubject();
        if (rig.HidesSubject)
        {
            subject.SetBodyHidden(true);
            _hiddenSubject = subject;
        }
        SetRig(rig);
        return rig;
    }

    public FollowRig? CycleFollowView() =>
        Subject is null ? null : Follow(Subject, FollowViewIndex + 1);

    /// <summary>Switch to any rig, including custom ones.</summary>
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

    private void RestoreHiddenSubject()
    {
        if (_hiddenSubject is not null && IsInstanceValid(_hiddenSubject.Body)) _hiddenSubject.SetBodyHidden(false);
        _hiddenSubject = null;
    }
}
