using Godot;

namespace CommandEngine.Core;

/// <summary>
/// Input action names the engine reads. <see cref="EnsureDefaults"/> registers
/// default bindings at startup for any action the game's project.godot does
/// not already define, so a game can rebind keys in its Input Map without
/// touching engine code.
/// </summary>
public static class InputActions
{
    public const string MoveForward = "ce_move_forward";
    public const string MoveBack = "ce_move_back";
    public const string MoveLeft = "ce_move_left";
    public const string MoveRight = "ce_move_right";
    public const string Jump = "ce_jump";
    public const string Fire = "ce_fire";
    public const string PilotToggle = "ce_pilot_toggle";
    public const string Cancel = "ce_cancel";
    public const string CycleCamera = "ce_cycle_camera";
    public const string RotateLeft = "ce_cam_rotate_left";
    public const string RotateRight = "ce_cam_rotate_right";
    public const string Stop = "ce_stop";

    public static void EnsureDefaults()
    {
        AddKeys(MoveForward, Key.W, Key.Up);
        AddKeys(MoveBack, Key.S, Key.Down);
        AddKeys(MoveLeft, Key.A, Key.Left);
        AddKeys(MoveRight, Key.D, Key.Right);
        AddKeys(Jump, Key.Space);
        AddKeys(PilotToggle, Key.F);
        AddKeys(Cancel, Key.Escape);
        AddKeys(CycleCamera, Key.V);
        AddKeys(RotateLeft, Key.Q);
        AddKeys(RotateRight, Key.E);
        AddKeys(Stop, Key.H);

        if (!InputMap.HasAction(Fire))
        {
            InputMap.AddAction(Fire);
            InputMap.ActionAddEvent(Fire, new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        }
    }

    private static void AddKeys(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
