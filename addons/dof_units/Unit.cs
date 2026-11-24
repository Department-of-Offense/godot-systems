using System.Collections.Generic;
using Dof.Combat;
using Dof.Core;
using Godot;

namespace Dof.Units;

/// <summary>
/// A controllable combatant. Every physics tick it asks its controller for a
/// <see cref="UnitIntent"/> and applies it, so it never knows who is driving.
/// With no pilot the <see cref="AiController"/> runs (by default it works
/// through <see cref="Commands"/>). <see cref="BeginPilot"/> hands control to
/// any other <see cref="IUnitController"/>; the order queue waits meanwhile.
///
/// Author units as scenes: a Unit root with a CollisionShape3D, meshes, and
/// optional <see cref="Health"/> and <see cref="Weapon"/> children.
/// </summary>
[GlobalClass]
public partial class Unit : CharacterBody3D, ICombatant
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

    /// <summary>Fire back at enemies in range when the order queue is empty.</summary>
    [Export] public bool AutoEngageWhenIdle { get; set; } = true;

    /// <summary>Share of weapon range at which the AI stops closing in and starts shooting.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float EngageRangeFactor { get; set; } = 0.85f;

    /// <summary>
    /// Seconds between enemy scans for AI targeting. Lower reacts faster but
    /// costs more with many units. Each unit starts at a random phase so scans spread out.
    /// </summary>
    [Export(PropertyHint.Range, "0,2,0.05")] public float TargetScanInterval { get; set; } = 0.25f;

    /// <summary>Drop queued orders when a pilot hands the unit back.</summary>
    [Export] public bool ClearOrdersOnRelease { get; set; }

    public Health Health { get; private set; } = null!;
    public Weapon? Weapon { get; private set; }
    public CommandQueue Commands { get; } = new();

    /// <summary>The controller used when no pilot is attached. Replace for custom AI.</summary>
    public IUnitController AiController { get; set; } = new OrderController();
    public IUnitController? Pilot { get; private set; }
    public IUnitController Controller => Pilot ?? AiController;
    public bool IsPiloted => Pilot is not null;
    public bool IsAlive => Health is not null && Health.IsAlive;

    /// <summary>Current aim pitch in radians, positive is up.</summary>
    public float AimPitch { get; private set; }

    public float EngageRange => (Weapon?.Range ?? 0f) * EngageRangeFactor;
    public Vector3 EyePosition => GlobalPosition + Vector3.Up * EyeHeight;
    public Vector3 AimPoint => GlobalPosition + Vector3.Up * (EyeHeight * 0.7f);
    public Vector3 AimDirection => Math3D.DirectionFor(Rotation.Y, AimPitch);

    private readonly Dictionary<GeometryInstance3D, GeometryInstance3D.ShadowCastingSetting> _hiddenGeometry = new();
    private Unit? _cachedEnemy;
    private double _scanTimer = GD.Randf() * 0.25;
    private readonly float _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);

    public override void _EnterTree() => UnitRegistry.Register(this);

    public override void _ExitTree() => UnitRegistry.Unregister(this);

    public override void _Ready()
    {
        AddToGroup(UnitRegistry.Group);
        Health = FindDirectChild<Health>() ?? AddNew(new Health { Name = "Health" });
        Health.Died += _ => HandleDeath();
        Weapon = FindDirectChild<Weapon>();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsAlive) return;
        _scanTimer -= delta;
        ApplyIntent(Controller.ComputeIntent(this, delta), delta);
    }

    /// <summary>
    /// Nearest hostile within <see cref="EngageRange"/>, rescanned every
    /// <see cref="TargetScanInterval"/> seconds. A cached target is re-checked
    /// each call, so a dead or out-of-range target triggers an immediate rescan.
    /// </summary>
    public Unit? NearestEnemyInRange()
    {
        var range = EngageRange;
        var cachedValid = _cachedEnemy is not null && IsInstanceValid(_cachedEnemy) && _cachedEnemy.IsAlive
            && GlobalPosition.DistanceSquaredTo(_cachedEnemy.GlobalPosition) <= range * range;

        if (_scanTimer <= 0 || (_cachedEnemy is not null && !cachedValid))
        {
            _scanTimer = TargetScanInterval;
            _cachedEnemy = UnitRegistry.FindNearestEnemy(this, range);
            return _cachedEnemy;
        }
        return cachedValid ? _cachedEnemy : null;
    }

    /// <summary>Give an order. While piloted it is queued and starts once the pilot leaves.</summary>
    public void Issue(UnitCommand command, bool append = false) => Commands.Issue(command, append);

    public void BeginPilot(IUnitController pilot)
    {
        if (Pilot is not null) EndPilot();
        Pilot = pilot;
        pilot.OnAttached(this);
        EmitSignal(SignalName.PilotChanged, true);
    }

    public void EndPilot()
    {
        if (Pilot is null) return;
        var pilot = Pilot;
        Pilot = null;
        pilot.OnDetached(this);
        SetBodyHidden(false);
        if (ClearOrdersOnRelease) Commands.Clear();
        EmitSignal(SignalName.PilotChanged, false);
    }

    public bool IsFacing(Vector3 worldPoint, float toleranceRadians)
    {
        var to = Math3D.Flat(worldPoint - GlobalPosition);
        if (to.LengthSquared() < 0.0001f) return true;
        return Mathf.Abs(Mathf.AngleDifference(Rotation.Y, Math3D.YawFor(to))) <= toleranceRadians;
    }

    /// <summary>Hide this unit's meshes from cameras but keep their shadows (first-person views).</summary>
    public void SetBodyHidden(bool hidden)
    {
        if (hidden)
        {
            foreach (var geometry in Descendants<GeometryInstance3D>(this))
            {
                if (geometry.IsInGroup(NeverHideGroup) || _hiddenGeometry.ContainsKey(geometry)) continue;
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

    /// <summary>Put a mesh in this group to keep it visible in first person (e.g. arms, gun).</summary>
    public const string NeverHideGroup = "dof_never_hide";

    /// <summary>
    /// Default death: drop out of physics and topple over. Override for
    /// ragdolls, explosions, wreck swaps and so on. Call base to keep the
    /// bookkeeping (registry, orders, Died signal).
    /// </summary>
    protected virtual void OnDeath()
    {
        foreach (var geometry in Descendants<GeometryInstance3D>(this))
            if (geometry.MaterialOverride is StandardMaterial3D material)
                material.AlbedoColor = material.AlbedoColor.Darkened(0.7f);
        RotationDegrees = new Vector3(0f, RotationDegrees.Y, 80f);
    }

    private void HandleDeath()
    {
        Commands.Clear();
        RemoveFromGroup(UnitRegistry.Group);
        UnitRegistry.Unregister(this);
        CollisionLayer = 0;
        CollisionMask = 0;
        OnDeath();
        EmitSignal(SignalName.Died, this);
    }

    private void ApplyIntent(in UnitIntent intent, double delta)
    {
        var move = Math3D.Flat(intent.Move).LimitLength(1f) * MoveSpeed;
        var velocity = Velocity;
        velocity.X = move.X;
        velocity.Z = move.Z;
        velocity.Y = IsOnFloor() ? (intent.Jump ? JumpVelocity : 0f) : velocity.Y - _gravity * (float)delta;
        Velocity = velocity;
        MoveAndSlide();

        if (intent.FaceYaw is { } yaw)
        {
            var rotation = Rotation;
            rotation.Y = intent.SnapFacing ? yaw : Mathf.LerpAngle(rotation.Y, yaw, Math3D.Damp(TurnSharpness, delta));
            Rotation = rotation;
        }

        AimPitch = intent.AimPitch ?? Mathf.Lerp(AimPitch, 0f, Math3D.Damp(TurnSharpness, delta));

        if (intent.Fire && Weapon is not null)
        {
            var origin = EyePosition;
            var direction = intent.AimAt is { } target ? (target - origin).Normalized() : AimDirection;
            Weapon.TryFire(this, Team, origin, direction);
        }
    }

    private T? FindDirectChild<T>() where T : Node
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
