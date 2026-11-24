using Dof.Combat;
using Godot;

namespace Dof.Units;

/// <summary>
/// A unit type as data. Author as a .tres in the editor (New Resource →
/// UnitDefinition), then <see cref="Spawn"/> units from it, or set
/// <see cref="Unit.Definition"/> on a Unit in a scene.
///
/// A definition fills in whatever the unit doesn't already have: stats are
/// copied onto the unit, and a collider, model, Health and Weapon are added
/// only if missing. So a hand-built unit scene can use a definition just for
/// its numbers. Each unit gets its own copies, so changing one unit (an
/// upgrade, a debuff) never changes the shared resource.
///
/// Games subclass this to add their own fields (cost, build time, armour
/// class...) and override <see cref="Configure"/> or <see cref="CreateUnit"/>.
/// </summary>
[GlobalClass]
public partial class UnitDefinition : Resource
{
    /// <summary>Stable key for lookups and save files. Don't rename once shipped.</summary>
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "Unit";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Texture2D? Icon { get; set; }

    /// <summary>Free-form labels for game rules, e.g. "infantry", "vehicle", "air".</summary>
    [Export] public string[] Tags { get; set; } = [];

    [ExportGroup("Stats")]
    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MoveSpeed { get; set; } = 6f;
    [Export] public float TurnSharpness { get; set; } = 10f;
    [Export] public float JumpVelocity { get; set; } = 4.5f;

    [ExportGroup("Body")]
    /// <summary>Collider radius (metres), used when the unit has no collider of its own.</summary>
    [Export] public float Radius { get; set; } = 0.4f;
    [Export] public float Height { get; set; } = 1.8f;
    [Export] public float EyeHeight { get; set; } = 1.6f;

    /// <summary>
    /// Visuals, instanced as the unit's "Model" child. Facing is -Z. Put
    /// meshes that should take the team colour in the "dof_team_color" group.
    /// Leave empty for a placeholder capsule.
    /// </summary>
    [Export] public PackedScene? Model { get; set; }

    [ExportGroup("Combat")]
    [Export] public WeaponDefinition? Weapon { get; set; }

    /// <summary>Share of weapon range at which the AI stops closing in and starts shooting.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float EngageRangeFactor { get; set; } = 0.85f;
    [Export] public bool AutoEngageWhenIdle { get; set; } = true;

    /// <summary>Node group for meshes that take the team colour.</summary>
    public const string TeamColorGroup = "dof_team_color";

    public bool HasTag(string tag) => System.Array.IndexOf(Tags, tag) >= 0;

    /// <summary>
    /// A ready-to-add unit of this type. Add it to the scene tree yourself
    /// (after setting its position).
    /// </summary>
    public Unit Spawn(int team, Color? teamColor = null, string? name = null)
    {
        var unit = CreateUnit();
        unit.Name = name ?? (string.IsNullOrEmpty(Id) ? DisplayName : Id);
        unit.Team = team;
        unit.Definition = this;
        unit.ApplyDefinition();
        if (teamColor is { } color) ApplyTeamColor(unit, color);
        return unit;
    }

    /// <summary>Override to spawn your own Unit subclass.</summary>
    protected virtual Unit CreateUnit() => new();

    /// <summary>
    /// Copy stats onto <paramref name="unit"/> and add any missing parts.
    /// Called once per unit, by <see cref="Unit.ApplyDefinition"/>.
    /// Override to apply your own fields (call base first).
    /// </summary>
    public virtual void Configure(Unit unit)
    {
        unit.DisplayName = DisplayName;
        unit.MoveSpeed = MoveSpeed;
        unit.TurnSharpness = TurnSharpness;
        unit.JumpVelocity = JumpVelocity;
        unit.EyeHeight = EyeHeight;
        unit.EngageRangeFactor = EngageRangeFactor;
        unit.AutoEngageWhenIdle = AutoEngageWhenIdle;
        unit.Avoidance.Radius = Radius + 0.1f;
        unit.Avoidance.MaxSpeed = MoveSpeed;

        if (FindChild<CollisionShape3D>(unit) is null)
            unit.AddChild(new CollisionShape3D
            {
                Name = "Collision",
                Shape = new CapsuleShape3D { Radius = Radius, Height = Height },
                Position = new Vector3(0, Height / 2f, 0),
            });

        if (unit.GetNodeOrNull("Model") is null)
        {
            var model = Model?.Instantiate<Node3D>() ?? BuildPlaceholderModel();
            model.Name = "Model";
            unit.AddChild(model);
        }

        if (FindChild<Health>(unit) is { } health) health.MaxHealth = MaxHealth;
        else unit.AddChild(new Health { Name = "Health", MaxHealth = MaxHealth });

        if (Weapon is not null)
        {
            if (FindChild<Weapon>(unit) is { } weapon) Weapon.ApplyTo(weapon);
            else unit.AddChild(Weapon.CreateWeapon());
        }
    }

    /// <summary>
    /// Stand-in visuals for units without art: a capsule (team-coloured), a
    /// visor and, if armed, a barrel, so facing is readable from above.
    /// </summary>
    protected virtual Node3D BuildPlaceholderModel()
    {
        var root = new Node3D();
        var body = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = Radius, Height = Height },
            Position = new Vector3(0, Height / 2f, 0),
            MaterialOverride = Flat(new Color(0.8f, 0.8f, 0.8f)),
        };
        body.AddToGroup(TeamColorGroup);
        root.AddChild(body);
        root.AddChild(new MeshInstance3D
        {
            Name = "Visor",
            Mesh = new BoxMesh { Size = new Vector3(Radius * 1.25f, 0.15f, 0.2f) },
            Position = new Vector3(0, EyeHeight - 0.05f, -Radius * 0.8f),
            MaterialOverride = Flat(new Color(0.1f, 0.1f, 0.12f)),
        });
        if (Weapon is not null)
            root.AddChild(new MeshInstance3D
            {
                Name = "Barrel",
                Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.7f) },
                Position = new Vector3(Radius * 0.8f, Height * 0.67f, -Radius - 0.05f),
                MaterialOverride = Flat(new Color(0.2f, 0.2f, 0.22f)),
            });
        return root;
    }

    /// <summary>Tint every mesh in the <see cref="TeamColorGroup"/> under <paramref name="root"/>.</summary>
    public static void ApplyTeamColor(Node root, Color color)
    {
        foreach (var node in root.FindChildren("*", "GeometryInstance3D", true, false))
            if (node is GeometryInstance3D geometry && geometry.IsInGroup(TeamColorGroup))
                geometry.MaterialOverride = Flat(color);
    }

    private static StandardMaterial3D Flat(Color color) => new() { AlbedoColor = color, Roughness = 0.6f };

    private static T? FindChild<T>(Node parent) where T : Node
    {
        foreach (var child in parent.GetChildren())
            if (child is T match) return match;
        return null;
    }
}
