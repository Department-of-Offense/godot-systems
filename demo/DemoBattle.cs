using Dof.Hybrid;
using Dof.Prototyping;
using Dof.UI;
using Dof.Units;
using Godot;

namespace Dof.Demo;

/// <summary>
/// Playable sandbox using every system: a blue squad (yours) and a red squad
/// on a greybox field. Select blue units and order them around, or press F
/// to pilot one. Each squad mixes the unit types in demo/units/*.tres.
/// </summary>
public partial class DemoBattle : Node3D
{
    public override void _Ready()
    {
        Prototype.BuildBattlefield(this);

        var catalog = UnitCatalog.LoadFolder("res://demo/units");
        string[] squad = ["trooper", "scout", "heavy", "scout", "trooper"];

        for (var i = 0; i < squad.Length; i++)
            Spawn(catalog.Get(squad[i]), $"Blue-{i + 1}", 0, new Color(0.25f, 0.45f, 0.95f), new Vector3(-6 + i * 3, 0.1f, 25));

        for (var i = 0; i < squad.Length; i++)
        {
            var red = Spawn(catalog.Get(squad[i]), $"Red-{i + 1}", 1, new Color(0.9f, 0.25f, 0.2f), new Vector3(-6 + i * 3, 0.1f, -25));
            red.RotationDegrees = new Vector3(0, 180, 0);
        }

        var modes = new GameModeManager { Name = "GameModes", PlayerTeam = 0 };
        AddChild(modes);

        var hud = new CanvasLayer { Name = "Hud" };
        hud.AddChild(new CommandHud());
        AddChild(hud);

        // Dev shortcut: `godot --path . -- --pilot-view=0|1|2` starts already piloting Blue-1.
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--pilot-view=") || !int.TryParse(arg["--pilot-view=".Length..], out var view)) continue;
            modes.DefaultPilotView = view;
            Callable.From(() => modes.TryPilot(GetNode<Unit>("Blue-1"))).CallDeferred();
        }
    }

    private Unit Spawn(UnitDefinition type, string name, int team, Color color, Vector3 position)
    {
        var unit = Prototype.CreateUnit(type, name, team, color);
        unit.Position = position;
        AddChild(unit);
        return unit;
    }
}
