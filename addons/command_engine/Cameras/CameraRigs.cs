using CommandEngine.Controllers;
using CommandEngine.Core;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.Cameras;

/// <summary>
/// A way of placing the camera. Rigs only compute a transform; the
/// <see cref="CameraDirector"/> owns the actual Camera3D and blends between rigs.
/// </summary>
public interface ICameraRig
{
    string Name { get; }
    float Fov { get; }
    Transform3D Evaluate(double delta);
    void HandleInput(InputEvent @event) { }
}

/// <summary>Top-down command view: pan, rotate and zoom over the battlefield.</summary>
public sealed class StrategyCameraRig : ICameraRig
{
    public string Name => "Strategy";
    public float Fov => 55f;

    public Vector3 Focus { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; } = -0.95f;
    public float Distance { get; set; } = 35f;
    public float MinDistance { get; set; } = 8f;
    public float MaxDistance { get; set; } = 120f;
    public float PanSpeed { get; set; } = 1.1f; // scales with zoom distance
    public float RotateSpeed { get; set; } = 1.8f;
    public float ZoomStep { get; set; } = 1.15f;
    public bool AcceptsInput { get; set; } = true;

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
                Pitch = Mathf.Clamp(Pitch - motion.Relative.Y * 0.005f, -1.45f, -0.35f);
                break;
        }
    }

    public Transform3D Evaluate(double delta)
    {
        if (AcceptsInput)
        {
            var pan = Input.GetVector(
                InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);
            Focus += new Basis(Vector3.Up, Yaw) * new Vector3(pan.X, 0f, pan.Y) * (PanSpeed * Distance * (float)delta);

            var rotate = Input.GetAxis(InputActions.RotateRight, InputActions.RotateLeft);
            Yaw += rotate * RotateSpeed * (float)delta;
        }

        var basis = Basis.FromEuler(new Vector3(Pitch, Yaw, 0f));
        return new Transform3D(basis, Focus + basis * new Vector3(0f, 0f, Distance));
    }
}

/// <summary>Shared plumbing for views that follow a piloted unit.</summary>
public abstract class PilotRig : ICameraRig
{
    public abstract string Name { get; }
    public virtual float Fov => 75f;

    /// <summary>Whether the unit's body turns with the mouse in this view.</summary>
    public abstract bool BodyFollowsLook { get; }

    /// <summary>Whether the unit's own body should be hidden from the camera.</summary>
    public virtual bool HidesBody => false;

    protected Unit? Unit { get; private set; }
    protected PilotController? Pilot { get; private set; }

    /// <summary>
    /// Share of the arm the camera can use without passing through walls
    /// (0..1). Worked out in the physics step by <see cref="UpdateCollision"/>.
    /// </summary>
    public float ArmFraction { get; private set; } = 1f;

    public float CollisionRadius { get; set; } = 0.25f;

    public void Bind(Unit unit, PilotController pilot)
    {
        Unit = unit;
        Pilot = pilot;
        ArmFraction = 1f;
    }

    public virtual void HandleInput(InputEvent @event) { }

    /// <summary>Point the camera looks out from, before the arm pushes it back.</summary>
    protected abstract Vector3 Pivot(Unit unit, Vector3 unitOrigin);

    /// <summary>Camera offset from the pivot in look space. Zero for first person.</summary>
    protected abstract Vector3 Arm { get; }

    private Basis LookBasis => Basis.FromEuler(new Vector3(Pilot!.LookPitch, Pilot.LookYaw, 0f));

    public Transform3D Evaluate(double delta)
    {
        if (Unit is null || Pilot is null || !GodotObject.IsInstanceValid(Unit)) return Transform3D.Identity;
        var look = LookBasis;
        var pivot = Pivot(Unit, Unit.GetGlobalTransformInterpolated().Origin);
        return new Transform3D(look, pivot + look * (Arm * ArmFraction));
    }

    /// <summary>
    /// Spring arm: sphere-cast from the pivot toward the camera and pull the
    /// camera in if a wall is in the way. Call from the physics step only.
    /// </summary>
    public void UpdateCollision(PhysicsDirectSpaceState3D space, double delta)
    {
        if (Unit is null || Pilot is null || !GodotObject.IsInstanceValid(Unit) || Arm == Vector3.Zero)
        {
            ArmFraction = 1f;
            return;
        }

        var pivot = Pivot(Unit, Unit.GlobalPosition);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = CollisionRadius },
            Transform = new Transform3D(Basis.Identity, pivot),
            Motion = LookBasis * Arm,
            Exclude = new Godot.Collections.Array<Rid> { Unit.GetRid() },
        };
        var safe = space.CastMotion(query)[0];
        // Pull in instantly so we never see through a wall; ease back out.
        ArmFraction = safe < ArmFraction ? safe : Mathf.Lerp(ArmFraction, safe, UnitMath.Damp(6f, delta));
    }
}

public sealed class FirstPersonRig : PilotRig
{
    public override string Name => "First person";
    public override float Fov => 80f;
    public override bool BodyFollowsLook => true;
    public override bool HidesBody => true;

    protected override Vector3 Pivot(Unit unit, Vector3 unitOrigin) => unitOrigin + Vector3.Up * unit.EyeHeight;
    protected override Vector3 Arm => Vector3.Zero;
}

/// <summary>Over-the-shoulder view that stays behind the unit.</summary>
public sealed class ChaseRig : PilotRig
{
    public override string Name => "Chase";
    public override bool BodyFollowsLook => true;

    public float Distance { get; set; } = 4.5f;
    public float ShoulderOffset { get; set; } = 0.7f;
    public float PivotHeight { get; set; } = 0.3f; // above eye height

    protected override Vector3 Pivot(Unit unit, Vector3 unitOrigin) =>
        unitOrigin + Vector3.Up * (unit.EyeHeight + PivotHeight);
    protected override Vector3 Arm => new(ShoulderOffset, 0f, Distance);
}

/// <summary>Free orbit around the unit with zoom. The unit turns toward where it walks.</summary>
public sealed class OrbitRig : PilotRig
{
    public override string Name => "Orbit";
    public override bool BodyFollowsLook => false;

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

    protected override Vector3 Pivot(Unit unit, Vector3 unitOrigin) => unitOrigin + Vector3.Up * PivotHeight;
    protected override Vector3 Arm => new(0f, 0f, Distance);
}
