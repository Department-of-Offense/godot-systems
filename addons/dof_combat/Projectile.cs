using System.Collections.Generic;
using Godot;

namespace Dof.Combat;

/// <summary>
/// A shot with travel time: moves each physics tick, falls with
/// <see cref="Gravity"/>, and raycasts the segment it covered so fast shots
/// never tunnel through thin walls. On impact it damages what it hit and,
/// with a <see cref="SplashRadius"/>, everything nearby (less with distance).
///
/// <see cref="Weapon"/> launches these when its ProjectileSpeed is above 0.
/// You can also spawn one yourself (grenades, turrets): set the fields and add
/// it to the world. Damage is reported through <see cref="Source"/>'s Hit
/// signal while that weapon still exists.
/// </summary>
public partial class Projectile : Node3D
{
    public Weapon? Source { get; set; }
    public CollisionObject3D? Shooter { get; set; }
    public int Team { get; set; } = Teams.Neutral;
    public float Damage { get; set; } = 10f;
    public Vector3 Velocity { get; set; }
    public float Gravity { get; set; }
    public float MaxDistance { get; set; } = 100f;
    public uint HitMask { get; set; } = uint.MaxValue;
    public bool FriendlyFire { get; set; }
    public float SplashRadius { get; set; }
    public float SplashEdgeDamage { get; set; } = 0.25f;
    public Color Color { get; set; } = new(1f, 0.85f, 0.4f);

    /// <summary>Visual length of the bolt in metres.</summary>
    public float Length { get; set; } = 0.6f;

    private float _travelled;
    private MeshInstance3D? _bolt;

    public override void _Ready()
    {
        TopLevel = true;
        _bolt = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, Length) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = Glow(Color),
        };
        AddChild(_bolt);
        Orient();
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        Velocity += Vector3.Down * Gravity * dt;
        var from = GlobalPosition;
        var step = Velocity * dt;
        var to = from + step;

        var query = PhysicsRayQueryParameters3D.Create(from, to, HitMask);
        if (Shooter is not null && IsInstanceValid(Shooter)) query.Exclude = new Godot.Collections.Array<Rid> { Shooter.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            Impact((Vector3)hit["position"], hit["collider"].AsGodotObject());
            return;
        }

        GlobalPosition = to;
        Orient();
        _travelled += step.Length();
        if (_travelled >= MaxDistance)
        {
            // Splash weapons go off at the end of their range, like a timed fuse.
            if (SplashRadius > 0f) Impact(to, null);
            else QueueFree();
        }
    }

    private void Impact(Vector3 point, GodotObject? collider)
    {
        var direct = CombatQuery.FromCollider(collider);
        if (direct is not null) Report(direct, Damage);

        if (SplashRadius > 0f)
        {
            foreach (var (victim, distance) in InRadius(point, SplashRadius))
            {
                if (victim == direct) continue;
                var falloff = Mathf.Lerp(1f, SplashEdgeDamage, Mathf.Clamp(distance / SplashRadius, 0f, 1f));
                Report(victim, Damage * falloff);
            }
        }

        SpawnFlash(point);
        QueueFree();
    }

    private void Report(ICombatant victim, float amount)
    {
        if (Source is not null && IsInstanceValid(Source)) Source.DealDamage(victim, amount, Team, Shooter);
        else TryDamage(victim, amount, Team, FriendlyFire, Shooter);
    }

    /// <summary>Apply damage if alive and friendly-fire rules allow it. Shared by weapons and projectiles.</summary>
    public static bool TryDamage(ICombatant victim, float amount, int team, bool friendlyFire, Node? source)
    {
        if (amount <= 0f || !victim.Health.IsAlive) return false;
        if (!friendlyFire && !Teams.AreHostile(team, victim.Team)) return false;
        victim.Health.TakeDamage(amount, source);
        return true;
    }

    private IEnumerable<(ICombatant victim, float distance)> InRadius(Vector3 point, float radius)
    {
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, point),
            CollisionMask = HitMask,
        };
        var seen = new HashSet<ICombatant>();
        foreach (var result in GetWorld3D().DirectSpaceState.IntersectShape(query, 64))
        {
            if (result["collider"].AsGodotObject() is not Node node) continue;
            var victim = CombatQuery.FromCollider(node);
            if (victim is null || !seen.Add(victim)) continue;
            var position = victim is Node3D body ? body.GlobalPosition : point;
            yield return (victim, position.DistanceTo(point));
        }
    }

    private void Orient()
    {
        if (Velocity.LengthSquared() < 0.0001f) return;
        var forward = Velocity.Normalized();
        var up = Mathf.Abs(forward.Y) > 0.99f ? Vector3.Right : Vector3.Up;
        LookAt(GlobalPosition + forward, up);
    }

    private void SpawnFlash(Vector3 point)
    {
        var size = SplashRadius > 0f ? SplashRadius : 0.3f;
        var flash = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = size * 0.5f, Height = size },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = Glow(new Color(Color, 0.6f)),
            Position = point,
        };
        GetParent().AddChild(flash);
        GetTree().CreateTimer(SplashRadius > 0f ? 0.15 : 0.06).Timeout += flash.QueueFree;
    }

    private static StandardMaterial3D Glow(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
        Transparency = color.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };
}
