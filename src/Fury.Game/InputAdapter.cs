using Fury.Core.Sim;
using Godot;

namespace Fury.Game;

/// <summary>
/// Platform input adapter: converts Godot input actions, gamepads (D-pad and left stick) and on-screen
/// touch buttons (which drive the same actions) into the simulation's bit-packed <see cref="InputState"/>.
/// Always returns the RAW HELD state: the simulation detects press edges itself (<c>InputBuffer.Pressed</c>),
/// which is what keeps replays and rollback exact. Each binding set (0 = P1, 1 = P2) can be remapped from the
/// Godot InputMap at runtime.
/// </summary>
public static class InputAdapter
{
    private const float StickDeadzone = 0.5f;

    private static readonly (string Suffix, InputButton Button)[] Actions =
    [
        ("left", InputButton.Left),
        ("right", InputButton.Right),
        ("up", InputButton.Up),
        ("down", InputButton.Down),
        ("light", InputButton.Light),
        ("medium", InputButton.Medium),
        ("heavy", InputButton.Heavy),
        ("special", InputButton.Special),
        ("macro1", InputButton.Macro1),
        ("macro2", InputButton.Macro2),
        ("macro3", InputButton.Macro3),
        ("macro4", InputButton.Macro4),
    ];

    // Action names are built once: building "p1_left" strings every frame would allocate on the hot path.
    private static readonly StringName[][] ActionNames = BuildActionNames();

    private static StringName[][] BuildActionNames()
    {
        var names = new StringName[2][];
        for (int slot = 0; slot < 2; slot++)
        {
            names[slot] = new StringName[Actions.Length];
            for (int i = 0; i < Actions.Length; i++) names[slot][i] = new StringName($"p{slot + 1}_{Actions[i].Suffix}");
        }
        return names;
    }

    /// <summary>
    /// Polls the binding set of <paramref name="slot"/> (0 = P1 keys/pad 0, 1 = P2 keys/pad 1). In online play
    /// the local player always uses binding set 0, whichever fighter slot the netcode assigned.
    /// </summary>
    public static InputState Poll(int slot)
    {
        if (slot is not (0 or 1)) return new InputState(0);

        ushort bits = 0;
        var names = ActionNames[slot];
        for (int i = 0; i < Actions.Length; i++)
        {
            if (InputMap.HasAction(names[i]) && Input.IsActionPressed(names[i])) bits |= (ushort)Actions[i].Button;
        }
        bits |= PollGamepad(slot);
        return new InputState(bits);
    }

    private static ushort PollGamepad(int device)
    {

        ushort bits = 0;
        float x = Input.GetJoyAxis(device, JoyAxis.LeftX);
        float y = Input.GetJoyAxis(device, JoyAxis.LeftY);
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadLeft) || x < -StickDeadzone) bits |= (ushort)InputButton.Left;
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadRight) || x > StickDeadzone) bits |= (ushort)InputButton.Right;
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadUp) || y < -StickDeadzone) bits |= (ushort)InputButton.Up;
        if (Input.IsJoyButtonPressed(device, JoyButton.DpadDown) || y > StickDeadzone) bits |= (ushort)InputButton.Down;

        // Fight buttons follow the remappable pad layout (see InputBindings).
        var layout = InputBindings.PadLayout;
        for (int i = 0; i < layout.Count; i++)
        {
            if (Input.IsJoyButtonPressed(device, layout[i].Button)) bits |= (ushort)layout[i].Input;
        }
        return bits;
    }
}
