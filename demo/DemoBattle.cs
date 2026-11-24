using CommandEngine.Core;
using CommandEngine.Prototyping;
using CommandEngine.Units;
using Godot;

namespace CommandEngine.Demo;

/// <summary>
/// Playable sandbox: a blue squad (yours) and a red squad on a greybox field.
/// Select blue units and order them around, or press F to pilot one.
/// </summary>
public partial class DemoBattle : Node3D
{
    public override void _Ready()
    {
        Prototype.BuildBattlefield(this);

        for (var i = 0; i < 5; i++)
            SpawnTrooper($"Blue-{i + 1}", 0, new Color(0.25f, 0.45f, 0.95f), new Vector3(-6 + i * 3, 0.1f, 25));

        for (var i = 0; i < 5; i++)
        {
            var red = SpawnTrooper($"Red-{i + 1}", 1, new Color(0.9f, 0.25f, 0.2f), new Vector3(-6 + i * 3, 0.1f, -25));
            red.RotationDegrees = new Vector3(0, 180, 0);
        }

        var modes = new GameModeManager { Name = "GameModes", PlayerTeam = 0 };
        AddChild(modes);

        // Dev shortcut: `godot --path . -- --pilot-view=0|1|2` starts already piloting Blue-1.
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--pilot-view=") || !int.TryParse(arg["--pilot-view=".Length..], out var view)) continue;
            modes.DefaultPilotView = view;
            Callable.From(() => modes.TryPilot(GetNode<Unit>("Blue-1"))).CallDeferred();
        }
    }

    private Unit SpawnTrooper(string name, int team, Color color, Vector3 position)
    {
        var unit = Prototype.CreateTrooper(name, team, color);
        unit.Position = position;
        AddChild(unit);
        return unit;
    }
}
