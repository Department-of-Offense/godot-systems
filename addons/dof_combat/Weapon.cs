using System;
using Godot;

namespace Dof.Combat;

/// <summary>
/// A gun. Whoever holds it (an AI, a player, a turret script) just calls
/// <see cref="TryFire"/>; the weapon handles rate of fire, magazine and
/// reload, spread with bloom, pellets, recoil, and either instant hitscan
/// shots or <see cref="Projectile"/>s with travel time and splash.
///
/// Every feature is off by default (no spread, bottomless magazine, hitscan),
/// so a bare Weapon is a perfectly accurate laser; turn features on with a
/// <see cref="WeaponDefinition"/> or the exported fields. Subclass and
/// override <see cref="Discharge"/> for beams or other shot types.
/// </summary>
[GlobalClass]
public partial class Weapon : Node3D
{
    /// <summary>A shot (one per pellet) left the weapon. For projectiles, <paramref name="to"/> is where it would end at full range.</summary>
    [Signal] public delegate void FiredEventHandler(Vector3 from, Vector3 to, Node? hit);

    /// <summary>This weapon damaged something. Use for hit markers and kill feeds.</summary>
    [Signal] public delegate void HitEventHandler(Node? target, float damage, bool killed);

    [Signal] public delegate void AmmoChangedEventHandler(int ammo, int magazineSize);
    [Signal] public delegate void ReloadStartedEventHandler();
    [Signal] public delegate void ReloadFinishedEventHandler();

    /// <summary>
    /// Raised once per trigger pull with the recoil kick in radians
    /// (X = pitch up, Y = yaw). Whoever aims the weapon decides what to do
    /// with it; a player controller kicks the view.
    /// </summary>
    public event Action<Vector2>? Recoiled;

    /// <summary>When set, its values replace the ones below when the weapon becomes ready.</summary>
    [Export] public WeaponDefinition? Definition { get; set; }

    /// <summary>Damage per hit (per pellet for shotguns).</summary>
    [Export] public float Damage { get; set; } = 10f;
    [Export] public float FireInterval { get; set; } = 0.2f;
    [Export] public float Range { get; set; } = 40f;
    [Export] public bool FriendlyFire { get; set; }
    [Export(PropertyHint.Layers3DPhysics)] public uint HitMask { get; set; } = uint.MaxValue;
    [Export] public bool ShowTracers { get; set; } = true;
    [Export] public Color TracerColor { get; set; } = new(1f, 0.85f, 0.4f);

    [ExportGroup("Accuracy")]
    /// <summary>Half-angle of the shot cone, in degrees, before bloom.</summary>
    [Export(PropertyHint.Range, "0,30,0.1")] public float SpreadDegrees { get; set; }
    /// <summary>Extra spread added by each shot (bloom), in degrees.</summary>
    [Export(PropertyHint.Range, "0,10,0.05")] public float SpreadPerShot { get; set; }
    [Export(PropertyHint.Range, "0,45,0.5")] public float MaxSpreadDegrees { get; set; } = 10f;
    /// <summary>How fast bloom wears off, in degrees per second.</summary>
    [Export] public float SpreadRecovery { get; set; } = 10f;
    /// <summary>Shots per trigger pull (shotguns). Each pellet does full <see cref="Damage"/>.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int Pellets { get; set; } = 1;

    [ExportGroup("Recoil")]
    /// <summary>View kick upward per trigger pull, in degrees.</summary>
    [Export] public float RecoilPitchDegrees { get; set; }
    /// <summary>Random sideways view kick per trigger pull, up to this many degrees either way.</summary>
    [Export] public float RecoilYawDegrees { get; set; }

    [ExportGroup("Ammo")]
    /// <summary>Rounds per magazine. 0 = bottomless, never reloads.</summary>
    [Export] public int MagazineSize { get; set; }
    [Export] public float ReloadTime { get; set; } = 1.5f;
    /// <summary>Start reloading automatically when the magazine runs dry.</summary>
    [Export] public bool AutoReload { get; set; } = true;

    [ExportGroup("Projectile")]
    /// <summary>Metres per second. 0 = hitscan (instant).</summary>
    [Export] public float ProjectileSpeed { get; set; }
    /// <summary>Downward acceleration on projectiles (m/s²). 0 = flies straight.</summary>
    [Export] public float ProjectileGravity { get; set; }
    /// <summary>Damage radius around the impact point (metres). 0 = direct hits only.</summary>
    [Export] public float SplashRadius { get; set; }
    /// <summary>Share of damage dealt at the edge of the splash radius.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float SplashEdgeDamage { get; set; } = 0.25f;

    [ExportGroup("Aiming")]
    /// <summary>Spread multiplier while aiming down sights.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float AdsSpreadMultiplier { get; set; } = 0.5f;
    /// <summary>Camera zoom while aiming down sights (1 = none, 2 = half the field of view).</summary>
    [Export(PropertyHint.Range, "1,8,0.05")] public float AdsZoom { get; set; } = 1.35f;

    public int Ammo { get; private set; }
    public bool IsReloading => _reloadLeft > 0;
    /// <summary>0..1 while reloading, 1 otherwise.</summary>
    public float ReloadProgress => IsReloading && ReloadTime > 0 ? 1f - (float)(_reloadLeft / ReloadTime) : 1f;
    public bool HasBottomlessMagazine => MagazineSize <= 0;
    public bool IsReady => _cooldown <= 0 && !IsReloading && (HasBottomlessMagazine || Ammo > 0);

    private double _cooldown;
    private double _reloadLeft;
    private float _bloom;

    public override void _Ready()
    {
        Definition?.ApplyTo(this);
        Refill();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_cooldown > 0) _cooldown -= delta;
        if (_bloom > 0) _bloom = Mathf.Max(0f, _bloom - SpreadRecovery * (float)delta);
        if (_reloadLeft > 0)
        {
            _reloadLeft -= delta;
            if (_reloadLeft <= 0) FinishReload();
        }
    }

    /// <summary>Current cone half-angle in degrees, including bloom, times <paramref name="multiplier"/>.</summary>
    public float CurrentSpreadDegrees(float multiplier = 1f) =>
        Mathf.Min(SpreadDegrees + _bloom, Mathf.Max(MaxSpreadDegrees, SpreadDegrees)) * multiplier;

    /// <summary>Fill the magazine instantly and cancel any reload.</summary>
    public void Refill()
    {
        _reloadLeft = 0;
        Ammo = Math.Max(0, MagazineSize);
        EmitSignal(SignalName.AmmoChanged, Ammo, MagazineSize);
    }

    /// <summary>Start a reload. Does nothing if bottomless, full, or already reloading.</summary>
    public bool StartReload()
    {
        if (HasBottomlessMagazine || IsReloading || Ammo >= MagazineSize) return false;
        if (ReloadTime <= 0)
        {
            FinishReload();
            return true;
        }
        _reloadLeft = ReloadTime;
        EmitSignal(SignalName.ReloadStarted);
        return true;
    }

    private void FinishReload()
    {
        _reloadLeft = 0;
        Ammo = MagazineSize;
        EmitSignal(SignalName.AmmoChanged, Ammo, MagazineSize);
        EmitSignal(SignalName.ReloadFinished);
    }

    /// <summary>Fire if ready. Call from the physics step.</summary>
    /// <param name="shooter">Body to exclude from the shot (usually the holder).</param>
    /// <param name="team">Shooter's team, for friendly-fire checks.</param>
    /// <param name="spreadMultiplier">Scales spread for this shot: aiming down sights, moving, AI skill...</param>
    /// <returns>True if a shot was fired.</returns>
    public bool TryFire(CollisionObject3D? shooter, int team, Vector3 origin, Vector3 direction, float spreadMultiplier = 1f)
    {
        if (direction.LengthSquared() < 0.0001f || _cooldown > 0 || IsReloading) return false;
        if (!HasBottomlessMagazine && Ammo <= 0)
        {
            if (AutoReload) StartReload();
            return false;
        }

        _cooldown = FireInterval;
        if (!HasBottomlessMagazine)
        {
            Ammo--;
            EmitSignal(SignalName.AmmoChanged, Ammo, MagazineSize);
        }

        var aim = direction.Normalized();
        var spread = Mathf.DegToRad(CurrentSpreadDegrees(spreadMultiplier));
        for (var i = 0; i < Math.Max(1, Pellets); i++)
            Discharge(shooter, team, origin, Scatter(aim, spread));

        _bloom = Mathf.Min(_bloom + SpreadPerShot, Mathf.Max(0f, MaxSpreadDegrees - SpreadDegrees));
        if (RecoilPitchDegrees != 0f || RecoilYawDegrees != 0f)
            Recoiled?.Invoke(new Vector2(
                Mathf.DegToRad(RecoilPitchDegrees),
                Mathf.DegToRad(((float)Random.Shared.NextDouble() * 2f - 1f) * RecoilYawDegrees)));

        if (!HasBottomlessMagazine && Ammo <= 0 && AutoReload) StartReload();
        return true;
    }

    /// <summary>A random direction within <paramref name="halfAngle"/> radians of <paramref name="direction"/>, spread evenly over the cone's area.</summary>
    public static Vector3 Scatter(Vector3 direction, float halfAngle)
    {
        if (halfAngle <= 0f) return direction;
        var angle = halfAngle * Mathf.Sqrt((float)Random.Shared.NextDouble());
        var around = (float)Random.Shared.NextDouble() * Mathf.Tau;
        var side = direction.Cross(Mathf.Abs(direction.Y) > 0.99f ? Vector3.Right : Vector3.Up).Normalized();
        return direction.Rotated(side, angle).Rotated(direction, around).Normalized();
    }

    /// <summary>One shot (pellet), already scattered. Hitscan or projectile depending on <see cref="ProjectileSpeed"/>.</summary>
    protected virtual void Discharge(CollisionObject3D? shooter, int team, Vector3 origin, Vector3 direction)
    {
        if (ProjectileSpeed > 0f)
        {
            LaunchProjectile(shooter, team, origin, direction);
            EmitSignal(SignalName.Fired, origin, origin + direction * Range, (Node?)null!);
            return;
        }

        var end = origin + direction * Range;
        var query = PhysicsRayQueryParameters3D.Create(origin, end, HitMask);
        if (shooter is not null) query.Exclude = new Godot.Collections.Array<Rid> { shooter.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        Node? hitNode = null;
        if (hit.Count > 0)
        {
            end = (Vector3)hit["position"];
            hitNode = hit["collider"].AsGodotObject() as Node;
            ApplyHit(CombatQuery.FromCollider(hitNode), team, shooter);
        }

        if (ShowTracers) SpawnTracer(origin, end);
        EmitSignal(SignalName.Fired, origin, end, hitNode!);
    }

    protected virtual void LaunchProjectile(CollisionObject3D? shooter, int team, Vector3 origin, Vector3 direction)
    {
        var projectile = new Projectile
        {
            Source = this,
            Shooter = shooter,
            Team = team,
            Damage = Damage,
            Velocity = direction * ProjectileSpeed,
            Gravity = ProjectileGravity,
            MaxDistance = Range,
            HitMask = HitMask,
            FriendlyFire = FriendlyFire,
            SplashRadius = SplashRadius,
            SplashEdgeDamage = SplashEdgeDamage,
            Color = TracerColor,
            Position = origin,
        };
        WorldRoot().AddChild(projectile);
    }

    /// <summary>Full <see cref="Damage"/> to <paramref name="victim"/>, if allowed.</summary>
    protected void ApplyHit(ICombatant? victim, int team, Node? source)
    {
        if (victim is not null) DealDamage(victim, Damage, team, source);
    }

    /// <summary>
    /// Damage <paramref name="victim"/> if friendly-fire rules allow it, and
    /// report it through <see cref="Hit"/>. Projectiles call this too.
    /// </summary>
    public bool DealDamage(ICombatant victim, float amount, int team, Node? source)
    {
        if (!Projectile.TryDamage(victim, amount, team, FriendlyFire, source)) return false;
        EmitSignal(SignalName.Hit, (victim as Node)!, amount, !victim.Health.IsAlive);
        return true;
    }

    /// <summary>Where shots, tracers and projectiles live: in the world, not on the shooter, so they don't swing with it.</summary>
    protected Node WorldRoot() => GetTree().CurrentScene ?? GetTree().Root;

    protected void SpawnTracer(Vector3 from, Vector3 to)
    {
        var length = from.DistanceTo(to);
        if (length < 0.05f) return;

        var tracer = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.04f, length) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = TracerColor,
            },
        };
        WorldRoot().AddChild(tracer);
        var up = Mathf.Abs((to - from).Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up;
        tracer.LookAtFromPosition((from + to) * 0.5f, to, up);
        GetTree().CreateTimer(0.05).Timeout += tracer.QueueFree;
    }
}
