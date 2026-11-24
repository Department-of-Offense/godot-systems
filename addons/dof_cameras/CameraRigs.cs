using Dof.Core;
using Godot;

namespace Dof.Cameras;

/// <summary>
/// A way of placing the camera. Rigs only compute a transform; the
/// <see cref="CameraDirector"/> owns the Camera3D and blends between rigs.
/// Implement this for any custom camera (cinematic, replay, kill-cam...).
/// </summary>
public interface ICameraRig
{
    string Name { get; }
    float Fov { get; }
    Transform3D Evaluate(double delta);
    void HandleInput(InputEvent @event) { }
    /// <summary>Called in the physics step while this rig is active (safe for raycasts).</summary>
    void PhysicsUpdate(PhysicsDirectSpaceState3D space, double delta) { }
}

/// <summary>
/// Anything a follow camera can track: a soldier, a vehicle, a drone, a
/// spectator dummy. Follow rigs know nothing else about it.
/// </summary>
public interface ICameraSubject
{
    /// <summary>The node to follow. Its interpolated transform is used for smooth motion.</summary>
    Node3D Body { get; }

    /// <summary>Eye height above <see cref="Body"/>'s origin.</summary>
    float EyeHeight { get; }

    /// <summary>Where the subject is looking (radians; yaw 0 faces -Z, positive pitch looks up).</summary>
    float LookYaw { get; }
    float LookPitch { get; }

    /// <summary>Hide or show the subject's own body (first-person views). Optional.</summary>
    void SetBodyHidden(bool hidden) { }
}

/// <summary>Top-down command view: pan, rotate and zoom over the battlefield.</summary>
public sealed class StrategyCameraRig : ICameraRig
{
    public string Name => "Strategy";
    public float Fov { get; set; } = 55f;

    public Vector3 Focus { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; } = -0.95f;
    public float MinPitch { get; set; } = -1.45f;
    public float MaxPitch { get; set; } = -0.35f;
    public float Distance { get; set; } = 35f;
    public float MinDistance { get; set; } = 8f;
    public float MaxDistance { get; set; } = 120f;
    public float PanSpeed { get; set; } = 1.1f; // scales with zoom distance
    public float RotateSpeed { get; set; } = 1.8f;
    public float ZoomStep { get; set; } = 1.15f;
    public bool AcceptsInput { get; set; } = true;

    public string PanLeftAction { get; set; } = CoreActions.MoveLeft;
    public string PanRightAction { get; set; } = CoreActions.MoveRight;
    public string PanForwardAction { get; set; } = CoreActions.MoveForward;
    public string PanBackAction { get; set; } = CoreActions.MoveBack;
    public string RotateLeftAction { get; set; } = CameraActions.RotateLeft;
    public string RotateRightAction { get; set; } = CameraActions.RotateRight;

    private bool _dragRotating;

    public void HandleInput(InputEvent @event)
    {
        if (!AcceptsInput) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Distance = Mathf.Max(MinDistance, Distance / ZoomStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Distance = Mathf.Min(MaxDistance, Distance * ZoomStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } button:
                _dragRotating = button.Pressed;
                break;
            case InputEventMouseMotion motion when _dragRotating:
                Yaw -= motion.Relative.X * 0.005f;
                Pitch = Mathf.Clamp(Pitch - motion.Relative.Y * 0.005f, MinPitch, MaxPitch);
                break;
        }
    }

    public Transform3D Evaluate(double delta)
    {
        if (AcceptsInput)
        {
            var pan = Input.GetVector(PanLeftAction, PanRightAction, PanForwardAction, PanBackAction);
            Focus += new Basis(Vector3.Up, Yaw) * new Vector3(pan.X, 0f, pan.Y) * (PanSpeed * Distance * (float)delta);
            Yaw += Input.GetAxis(RotateRightAction, RotateLeftAction) * RotateSpeed * (float)delta;
        }

        var basis = Basis.FromEuler(new Vector3(Pitch, Yaw, 0f));
        return new Transform3D(basis, Focus + basis * new Vector3(0f, 0f, Distance));
    }
}

/// <summary>
/// Base for views that follow an <see cref="ICameraSubject"/>: a pivot on the
/// subject plus an arm in look space, with spring-arm collision so the camera
/// never ends up behind a wall. Subclass to make new follow views.
/// </summary>
public abstract class FollowRig : ICameraRig
{
    public abstract string Name { get; }

    /// <summary>Field of view with zoom applied. Override <see cref="BaseFov"/> to change the unzoomed value.</summary>
    public float Fov => BaseFov / _zoom;

    /// <summary>Field of view at zoom 1, in degrees.</summary>
    protected virtual float BaseFov => 75f;

    /// <summary>
    /// Target zoom (1 = none, 2 = half the field of view), e.g. for aiming
    /// down sights. The view eases toward it at <see cref="ZoomSharpness"/>.
    /// </summary>
    public float Zoom { get; set; } = 1f;

    public float ZoomSharpness { get; set; } = 14f;

    /// <summary>The eased zoom actually in use this frame.</summary>
    public float CurrentZoom => _zoom;

    private float _zoom = 1f;

    /// <summary>
    /// True if the camera looks around independently of the subject's facing
    /// (orbit). False if the subject should turn with the view (first person, chase).
    /// </summary>
    public virtual bool FreeLook => false;

    /// <summary>Whether the subject's body should be hidden from this view.</summary>
    public virtual bool HidesSubject => false;

    public float CollisionRadius { get; set; } = 0.25f;

    /// <summary>Share of the arm the camera can use without clipping (0..1).</summary>
    public float ArmFraction { get; private set; } = 1f;

    public ICameraSubject? Subject { get; private set; }

    public void Follow(ICameraSubject? subject)
    {
        Subject = subject;
        ArmFraction = 1f;
        Zoom = 1f;
        _zoom = 1f;
    }

    public virtual void HandleInput(InputEvent @event) { }

    /// <summary>Point the camera looks out from, before the arm pushes it back.</summary>
    protected abstract Vector3 Pivot(ICameraSubject subject, Vector3 bodyOrigin);

    /// <summary>Camera offset from the pivot in look space. Zero for first person.</summary>
    protected abstract Vector3 Arm { get; }

    private bool HasSubject => Subject is not null && GodotObject.IsInstanceValid(Subject.Body);

    private static Basis Look(ICameraSubject s) => Basis.FromEuler(new Vector3(s.LookPitch, s.LookYaw, 0f));

    public Transform3D Evaluate(double delta)
    {
        _zoom = Mathf.Lerp(_zoom, Mathf.Max(1f, Zoom), Math3D.Damp(ZoomSharpness, delta));
        if (!HasSubject) return Transform3D.Identity;
        var look = Look(Subject!);
        var pivot = Pivot(Subject!, Subject!.Body.GetGlobalTransformInterpolated().Origin);
        return new Transform3D(look, pivot + look * (Arm * ArmFraction));
    }

    public void PhysicsUpdate(PhysicsDirectSpaceState3D space, double delta)
    {
        if (!HasSubject || Arm == Vector3.Zero)
        {
            ArmFraction = 1f;
            return;
        }

        var pivot = Pivot(Subject!, Subject!.Body.GlobalPosition);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = CollisionRadius },
            Transform = new Transform3D(Basis.Identity, pivot),
            Motion = Look(Subject!) * Arm,
        };
        if (Subject!.Body is CollisionObject3D body)
            query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };

        var safe = space.CastMotion(query)[0];
        // Pull in instantly so we never see through a wall; ease back out.
        ArmFraction = safe < ArmFraction ? safe : Mathf.Lerp(ArmFraction, safe, Math3D.Damp(6f, delta));
    }
}

public sealed class FirstPersonRig : FollowRig
{
    public override string Name => "First person";
    protected override float BaseFov => 80f;
    public override bool HidesSubject => true;

    protected override Vector3 Pivot(ICameraSubject subject, Vector3 bodyOrigin) => bodyOrigin + Vector3.Up * subject.EyeHeight;
    protected override Vector3 Arm => Vector3.Zero;
}

/// <summary>Over-the-shoulder view that stays behind the subject.</summary>
public sealed class ChaseRig : FollowRig
{
    public override string Name => "Chase";

    public float Distance { get; set; } = 4.5f;
    public float ShoulderOffset { get; set; } = 0.7f;
    public float PivotHeight { get; set; } = 0.3f; // above eye height

    protected override Vector3 Pivot(ICameraSubject subject, Vector3 bodyOrigin) =>
        bodyOrigin + Vector3.Up * (subject.EyeHeight + PivotHeight);
    protected override Vector3 Arm => new(ShoulderOffset, 0f, Distance);
}

/// <summary>Free orbit around the subject with zoom.</summary>
public sealed class OrbitRig : FollowRig
{
    public override string Name => "Orbit";
    public override bool FreeLook => true;

    public float Distance { get; set; } = 8f;
    public float MinDistance { get; set; } = 2.5f;
    public float MaxDistance { get; set; } = 30f;
    public float PivotHeight { get; set; } = 1.2f;

    public override void HandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp) Distance = Mathf.Max(MinDistance, Distance / 1.15f);
            if (button.ButtonIndex == MouseButton.WheelDown) Distance = Mathf.Min(MaxDistance, Distance * 1.15f);
        }
    }

    protected override Vector3 Pivot(ICameraSubject subject, Vector3 bodyOrigin) => bodyOrigin + Vector3.Up * PivotHeight;
    protected override Vector3 Arm => new(0f, 0f, Distance);
}

public static class CameraActions
{
    public const string RotateLeft = "dof_cam_rotate_left";
    public const string RotateRight = "dof_cam_rotate_right";

    public static void EnsureDefaults()
    {
        CoreActions.EnsureDefaults();
        InputDefaults.Keys(RotateLeft, Key.Q);
        InputDefaults.Keys(RotateRight, Key.E);
    }
}
