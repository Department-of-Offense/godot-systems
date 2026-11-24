using System.Collections.Generic;
using Dof.Combat;
using Dof.Core;
using Dof.Navigation;
using Godot;

namespace Dof.Units;

/// <summary>
/// A controllable combatant. Every physics tick it asks its controller for a
/// <see cref="UnitIntent"/> and applies it, so it never knows who is driving.
/// With no pilot the <see cref="AiController"/> runs (by default it works
/// through <see cref="Commands"/>). <see cref="BeginPilot"/> hands control to
/// any other <see cref="IUnitController"/>; the order queue waits meanwhile.
///
/// Make units from a <see cref="UnitDefinition"/> (<c>definition.Spawn(team)</c>),
/// or author them as scenes: a Unit root with a CollisionShape3D, meshes, and
/// optional <see cref="Health"/> and <see cref="Weapon"/> children. A scene
/// unit can also point at a definition to take its stats and missing parts.
/// </summary>
[GlobalClass]
public partial class Unit : CharacterBody3D, ICombatant
{
    [Signal] public delegate void DiedEventHandler(Unit unit);
    [Signal] public delegate void PilotChangedEventHandler(bool piloted);

    /// <summary>
    /// The unit type this unit was made from. Its stats override the values
    /// below, and it adds a collider, model, Health and Weapon if missing.
    /// Applied once, when the unit is spawned or becomes ready.
    /// </summary>
    [Export] public UnitDefinition? Definition { get; set; }

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

    /// <summary>
    /// Follow navmesh paths when moving under orders. Units walk straight
    /// lines anyway when the world has no navmesh.
    /// </summary>
    [Export] public bool UseNavigation { get; set; } = true;

    /// <summary>
    /// A move order also finishes if the unit is this close (metres) and has
    /// been blocked for <see cref="CrowdedSettleSeconds"/>, e.g. by squadmates
    /// standing around the spot. 0 = always walk all the way.
    /// </summary>
    [Export] public float CrowdedArrivalRadius { get; set; } = 3f;

    [Export] public float CrowdedSettleSeconds { get; set; } = 1f;

    /// <summary>Steer around other units (RVO). See <see cref="Avoidance"/>.</summary>
    [Export] public bool UseAvoidance { get; set; } = true;

    /// <summary>Spread multiplier while walking (shooting on the move is less accurate).</summary>
    [Export(PropertyHint.Range, "1,4,0.05")] public float MovingSpreadMultiplier { get; set; } = 1.5f;

    /// <summary>
    /// Spread multiplier for this unit's AI (not for a pilot): below 1 is a
    /// sharpshooter, above 1 a poor shot. Use it for difficulty or veterancy.
    /// </summary>
    [Export(PropertyHint.Range, "0,4,0.05")] public float AiSpreadMultiplier { get; set; } = 1f;

    /// <summary>Drop queued orders when a pilot hands the unit back.</summary>
    [Export] public bool ClearOrdersOnRelease { get; set; }

    public Health Health { get; private set; } = null!;
    public Weapon? Weapon { get; private set; }
    public CommandQueue Commands { get; } = new();

    /// <summary>Path state for orders. Tune re-plan rate or navigation layers here.</summary>
    public PathFollower Path { get; } = new();

    /// <summary>
    /// Local avoidance so units under orders steer around each other instead
    /// of clumping. Piloted units ignore it but are still avoided by others.
    /// Turn off with <see cref="UseAvoidance"/>.
    /// </summary>
    public AvoidanceAgent Avoidance { get; } = new();

    /// <summary>The controller used when no pilot is attached. Replace for custom AI.</summary>
    public IUnitController AiController { get; set; } = new OrderController();
    public IUnitController? Pilot { get; private set; }
    public IUnitController Controller => Pilot ?? AiController;
    public bool IsPiloted => Pilot is not null;
    public bool IsAlive => Health is not null && Health.IsAlive;

    /// <summary>True while the driver is aiming down sights.</summary>
    public bool IsAimingDownSights { get; private set; }

    /// <summary>Spread multiplier the next shot would use (aiming, moving, AI skill).</summary>
    public float SpreadMultiplier
    {
        get
        {
            var multiplier = IsPiloted ? 1f : AiSpreadMultiplier;
            if (IsAimingDownSights && Weapon is not null) multiplier *= Weapon.AdsSpreadMultiplier;
            if (Math3D.Flat(Velocity).LengthSquared() > 0.25f) multiplier *= MovingSpreadMultiplier;
            return multiplier;
        }
    }

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

    public override void _EnterTree()
    {
        UnitRegistry.Register(this);
        if (UseAvoidance && !_died) // Health isn't set up yet on first enter, so don't ask IsAlive
        {
            Avoidance.MaxSpeed = MoveSpeed;
            Avoidance.Attach(GetWorld3D());
        }
    }

    public override void _ExitTree()
    {
        UnitRegistry.Unregister(this);
        Avoidance.Detach();
    }

    /// <summary>
    /// Apply <see cref="Definition"/> now (once). Spawn does this for you;
    /// call it yourself if you need a scene unit's parts before it is ready.
    /// </summary>
    public void ApplyDefinition()
    {
        if (_definitionApplied || Definition is null) return;
        _definitionApplied = true;
        Definition.Configure(this);
        Avoidance.ApplySettings();
    }

    private bool _definitionApplied;

    public override void _Ready()
    {
        ApplyDefinition();
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

    private bool _died;

    private void HandleDeath()
    {
        _died = true;
        Commands.Clear();
        RemoveFromGroup(UnitRegistry.Group);
        UnitRegistry.Unregister(this);
        Avoidance.Detach();
        Path.Reset();
        CollisionLayer = 0;
        CollisionMask = 0;
        OnDeath();
        EmitSignal(SignalName.Died, this);
    }

    private void ApplyIntent(in UnitIntent intent, double delta)
    {
        var move = Math3D.Flat(intent.Move).LimitLength(1f) * MoveSpeed;
        if (Avoidance.IsAttached)
        {
            // Agents ignore lower-priority agents. Moving units avoid each
            // other and the pilot; standing units step aside for movers
            // (a line of standing units would otherwise wall movers off).
            Avoidance.Priority = Pilot is not null ? 1f : move.LengthSquared() > 0.0001f ? 0.6f : 0.3f;
            move = Avoidance.Steer(GlobalPosition, move, obey: Pilot is null);
        }
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

        IsAimingDownSights = intent.AimDownSights;
        if (intent.Reload) Weapon?.StartReload();

        if (intent.Fire && Weapon is not null)
        {
            var origin = EyePosition;
            var direction = intent.AimAt is { } target ? (target - origin).Normalized() : AimDirection;
            Weapon.TryFire(this, Team, origin, direction, SpreadMultiplier);
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
