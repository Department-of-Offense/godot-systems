using Godot;

namespace Dof.Core;

/// <summary>
/// Registers fallback input bindings at runtime. Each system calls these for
/// the actions it reads. An action the game already defines in its Input Map
/// is left untouched, so games rebind keys in Project Settings, not code.
/// </summary>
public static class InputDefaults
{
    public static void Keys(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    public static void Mouse(string action, MouseButton button)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}

/// <summary>
/// Directional actions several systems share: strategy cameras pan with them,
/// piloted units walk with them. Systems take these as defaults but expose
/// their own exported action names, so a game can split them apart.
/// </summary>
public static class CoreActions
{
    public const string MoveForward = "dof_move_forward";
    public const string MoveBack = "dof_move_back";
    public const string MoveLeft = "dof_move_left";
    public const string MoveRight = "dof_move_right";

    public static void EnsureDefaults()
    {
        InputDefaults.Keys(MoveForward, Key.W, Key.Up);
        InputDefaults.Keys(MoveBack, Key.S, Key.Down);
        InputDefaults.Keys(MoveLeft, Key.A, Key.Left);
        InputDefaults.Keys(MoveRight, Key.D, Key.Right);
    }
}
