using System.Collections.Generic;
using CommandEngine.Controllers;
using Godot;

namespace CommandEngine.Units;

/// <summary>
/// A controllable combatant. Each physics tick it asks its current controller
/// for a <see cref="UnitIntent"/> and applies it. With no pilot, the controller
/// is the AI <see cref="CommandController"/>, which works through the unit's
/// <see cref="Commands"/> queue. When a player possesses the unit, a
/// <see cref="PilotController"/> takes over and the queue waits.
/// </summary>
[GlobalClass]
public partial class Unit : CharacterBody3D
{
    [Signal] public delegate void DiedEventHandler(Unit unit);
    [Signal] public delegate void PilotChangedEventHandler(bool piloted);

    [Export] public string DisplayName { get; set; } = "Unit";
    [Export] public int Team { get; set; }
    [Export] public float MoveSpeed { get; set; } = 6f;
    [Export] public float TurnSharpness { get; set; } = 10f;
    [Export] public float JumpVelocity { get; set; } = 4.5f;
    [Export] public float EyeHeight { get; set; } = 1.6f;
    [Export] public float ArrivalRadius { get; set; } = 0.5f;
    [Export] public float SelectionRadius { get; set; } = 0.8f;

    /// <summary>When the pilot leaves, drop the orders the unit had before.</summary>
    [Export] public bool ClearOrdersOnRelease { get; set; }

    public Health Health { get; private set; } = null!;
    public Weapon? Weapon { get; private set; }
    public CommandQueue Commands { get; } = new();
    public CommandController Ai { get; } = new();
    public PilotController? Pilot { get; private set; }
    public IUnitController Controller => Pilot is not null ? Pilot : Ai;
    public bool IsPiloted => Pilot is not null;
    public bool IsAlive => Health is not null && Health.IsAlive;

    /// <summary>Current aim pitch in radians, positive is up.</summary>
    public float AimPitch { get; private set; }

    public Vector3 EyePosition => GlobalPosition + Vector3.Up * EyeHeight;
    public Vector3 AimPoint => GlobalPosition + Vector3.Up * (EyeHeight * 0.7f);
    public Vector3 AimDirection => UnitMath.DirectionFor(Rotation.Y, AimPitch);

    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            if (_selectionRing is not null) _selectionRing.Visible = value && IsAlive;
        }
    }

    private MeshInstance3D? _selectionRing;
    private readonly Dictionary<GeometryInstance3D, GeometryInstance3D.ShadowCastingSetting> _hiddenGeometry = new();
    private readonly float _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);

    public override void _Ready()
    {
        AddToGroup(UnitRegistry.Group);

        Health = FindChild<Health>() ?? AddNew(new Health { Name = "Health" });
        Health.Died += _ => OnDied();
        Weapon = FindChild<Weapon>();

        _selectionRing = BuildSelectionRing();
        AddChild(_selectionRing);
        _selectionRing.Visible = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsAlive) return;
        var intent = Controller.ComputeIntent(this, delta);
        ApplyIntent(intent, delta);
    }

    /// <summary>
    /// Give this unit an order. While piloted the order is queued, and the AI
    /// starts it as soon as the pilot leaves.
    /// </summary>
    public void Issue(UnitCommand command, bool append = false) => Commands.Issue(command, append);

    public void BeginPilot(PilotController pilot)
    {
        Pilot = pilot;
        pilot.Attach(this);
        EmitSignal(SignalName.PilotChanged, true);
    }

    public void EndPilot()
    {
        if (Pilot is null) return;
        Pilot = null;
        SetFirstPersonHidden(false);
        if (ClearOrdersOnRelease) Commands.Clear();
        EmitSignal(SignalName.PilotChanged, false);
    }

    public bool IsFacing(Vector3 worldPoint, float toleranceRadians)
    {
        var to = UnitMath.Flat(worldPoint - GlobalPosition);
        if (to.LengthSquared() < 0.0001f) return true;
        return Mathf.Abs(Mathf.AngleDifference(Rotation.Y, UnitMath.YawFor(to))) <= toleranceRadians;
    }

    /// <summary>Hide the unit's own body from the camera in first person, but keep its shadow.</summary>
    public void SetFirstPersonHidden(bool hidden)
    {
        if (hidden)
        {
            foreach (var geometry in Descendants<GeometryInstance3D>(this))
            {
                if (geometry == _selectionRing || _hiddenGeometry.ContainsKey(geometry)) continue;
                _hiddenGeometry[geometry] = geometry.CastShadow;
                geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            }
        }
        else
        {
            foreach (var (geometry, setting) in _hiddenGeometry)
                if (IsInstanceValid(geometry)) geometry.CastShadow = setting;
            _hiddenGeometry.Clear();
        }
    }

    private void ApplyIntent(in UnitIntent intent, double delta)
    {
        var move = UnitMath.Flat(intent.Move).LimitLength(1f) * MoveSpeed;
        var velocity = Velocity;
        velocity.X = move.X;
        velocity.Z = move.Z;

        if (IsOnFloor())
            velocity.Y = intent.Jump ? JumpVelocity : 0f;
        else
            velocity.Y -= _gravity * (float)delta;

        Velocity = velocity;
        MoveAndSlide();

        if (intent.FaceYaw is { } yaw)
        {
            var rotation = Rotation;
            rotation.Y = intent.SnapFacing ? yaw : Mathf.LerpAngle(rotation.Y, yaw, UnitMath.Damp(TurnSharpness, delta));
            Rotation = rotation;
        }

        AimPitch = intent.AimPitch ?? Mathf.Lerp(AimPitch, 0f, UnitMath.Damp(TurnSharpness, delta));

        if (intent.Fire && Weapon is not null)
        {
            var origin = EyePosition;
            var direction = intent.AimAt is { } target ? (target - origin).Normalized() : AimDirection;
            Weapon.TryFire(this, origin, direction);
        }
    }

    private void OnDied()
    {
        IsSelected = false;
        Commands.Clear();
        RemoveFromGroup(UnitRegistry.Group);
        // Drop out of physics so the wreck doesn't block shots or movement.
        CollisionLayer = 0;
        CollisionMask = 0;
        foreach (var geometry in Descendants<GeometryInstance3D>(this))
            if (geometry.MaterialOverride is StandardMaterial3D material)
                material.AlbedoColor = material.AlbedoColor.Darkened(0.7f);
        RotationDegrees = new Vector3(0f, RotationDegrees.Y, 80f);
        EmitSignal(SignalName.Died, this);
    }

    private MeshInstance3D BuildSelectionRing() => new()
    {
        Name = "SelectionRing",
        Mesh = new TorusMesh { InnerRadius = SelectionRadius * 0.85f, OuterRadius = SelectionRadius, Rings = 32 },
        Position = new Vector3(0f, 0.05f, 0f),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.3f, 1f, 0.4f),
        },
    };

    private T? FindChild<T>() where T : Node
    {
        foreach (var child in GetChildren())
            if (child is T match) return match;
        return null;
    }

    private T AddNew<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }

    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
