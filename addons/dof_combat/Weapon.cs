using Godot;

namespace Dof.Combat;

/// <summary>
/// Hitscan weapon with a cooldown. Whoever holds it (an AI, a player, a
/// turret script) just calls <see cref="TryFire"/>. Subclass and override
/// <see cref="Discharge"/> for projectiles, spread, beams and so on.
/// </summary>
[GlobalClass]
public partial class Weapon : Node3D
{
    [Signal] public delegate void FiredEventHandler(Vector3 from, Vector3 to, Node? hit);

    /// <summary>When set, its values replace the ones below when the weapon becomes ready.</summary>
    [Export] public WeaponDefinition? Definition { get; set; }

    [Export] public float Damage { get; set; } = 10f;
    [Export] public float FireInterval { get; set; } = 0.2f;
    [Export] public float Range { get; set; } = 40f;
    [Export] public bool FriendlyFire { get; set; }
    [Export(PropertyHint.Layers3DPhysics)] public uint HitMask { get; set; } = uint.MaxValue;
    [Export] public bool ShowTracers { get; set; } = true;
    [Export] public Color TracerColor { get; set; } = new(1f, 0.85f, 0.4f);

    private double _cooldown;

    public bool IsReady => _cooldown <= 0;

    public override void _Ready() => Definition?.ApplyTo(this);

    public override void _PhysicsProcess(double delta)
    {
        if (_cooldown > 0) _cooldown -= delta;
    }

    /// <summary>Fire if off cooldown. Call from the physics step.</summary>
    /// <param name="shooter">Body to exclude from the shot (usually the holder).</param>
    /// <param name="team">Shooter's team, for friendly-fire checks.</param>
    /// <returns>True if a shot was fired.</returns>
    public bool TryFire(CollisionObject3D? shooter, int team, Vector3 origin, Vector3 direction)
    {
        if (!IsReady || direction.LengthSquared() < 0.0001f) return false;
        _cooldown = FireInterval;
        Discharge(shooter, team, origin, direction.Normalized());
        return true;
    }

    protected virtual void Discharge(CollisionObject3D? shooter, int team, Vector3 origin, Vector3 direction)
    {
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

    protected void ApplyHit(ICombatant? victim, int team, Node? source)
    {
        if (victim is null) return;
        if (FriendlyFire || Teams.AreHostile(team, victim.Team)) victim.Health.TakeDamage(Damage, source);
    }

    protected void SpawnTracer(Vector3 from, Vector3 to)
    {
        var length = from.DistanceTo(to);
        var scene = GetTree().CurrentScene;
        if (length < 0.05f || scene is null) return;

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
        // Tracers live in the world, not on the shooter, so they don't swing with it.
        scene.AddChild(tracer);
        var up = Mathf.Abs((to - from).Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up;
        tracer.LookAtFromPosition((from + to) * 0.5f, to, up);
        GetTree().CreateTimer(0.05).Timeout += tracer.QueueFree;
    }
}
