using Dof.Combat;
using Dof.Units;
using Godot;

namespace Dof.Prototyping;

/// <summary>
/// Greybox helpers for building test battlefields in code before any art
/// exists. Real games will author units and maps as scenes instead.
/// </summary>
public static class Prototype
{
    public const uint WorldLayer = 1;
    public const uint UnitLayer = 2;

    public static Unit CreateTrooper(string name, int team, Color color)
    {
        var unit = new Unit
        {
            Name = name,
            DisplayName = name,
            Team = team,
            CollisionLayer = UnitLayer,
            CollisionMask = WorldLayer | UnitLayer,
        };

        unit.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });

        var body = Material(color);
        unit.AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
            MaterialOverride = body,
        });
        // Visor and gun barrel make the facing direction (-Z) obvious.
        unit.AddChild(new MeshInstance3D
        {
            Name = "Visor",
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.15f, 0.2f) },
            Position = new Vector3(0, 1.55f, -0.32f),
            MaterialOverride = Material(new Color(0.1f, 0.1f, 0.12f)),
        });
        unit.AddChild(new MeshInstance3D
        {
            Name = "Gun",
            Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.7f) },
            Position = new Vector3(0.32f, 1.2f, -0.45f),
            MaterialOverride = Material(new Color(0.2f, 0.2f, 0.22f)),
        });

        unit.AddChild(new Health { Name = "Health", MaxHealth = 100f });
        unit.AddChild(new Weapon { Name = "Weapon", Damage = 8f, FireInterval = 0.25f, Range = 35f });
        return unit;
    }

    /// <summary>Flat ground, sun, sky and a scatter of cover blocks.</summary>
    public static void BuildBattlefield(Node3D root, float size = 160f, int coverCount = 24, int seed = 7)
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        root.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });

        root.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            RotationDegrees = new Vector3(-50, -30, 0),
        });

        var ground = new StaticBody3D { Name = "Ground", CollisionLayer = WorldLayer };
        ground.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(size, 1, size) },
            Position = new Vector3(0, -0.5f, 0),
        });
        ground.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            MaterialOverride = Material(new Color(0.32f, 0.34f, 0.3f)),
        });
        root.AddChild(ground);

        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var coverMaterial = Material(new Color(0.45f, 0.42f, 0.38f));
        for (var i = 0; i < coverCount; i++)
        {
            var extent = new Vector3(rng.RandfRange(1.5f, 5f), rng.RandfRange(1f, 3f), rng.RandfRange(1.5f, 5f));
            var position = new Vector3(rng.RandfRange(-size * 0.35f, size * 0.35f), extent.Y / 2f, rng.RandfRange(-size * 0.35f, size * 0.35f));
            // Keep the middle lane open so the two sides can see each other.
            if (Mathf.Abs(position.X) < 6f) position.X += 12f * Mathf.Sign(position.X == 0 ? 1 : position.X);

            var block = new StaticBody3D { Name = $"Cover{i}", CollisionLayer = WorldLayer, Position = position };
            block.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = extent } });
            block.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = extent }, MaterialOverride = coverMaterial });
            root.AddChild(block);
        }
    }

    public static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color, Roughness = 0.8f };
}
