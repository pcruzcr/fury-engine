using Fury.Core.Sim;
using Godot;

namespace Fury.Game;

/// <summary>
/// Remappable controls: every fight button of both players on the keyboard, and every button of the gamepads, persisted in
/// <c>user://input.cfg</c>. Keyboard bindings live in Godot's <see cref="InputMap"/> (actions <c>p1_light</c>, <c>p2_special</c>...);
/// the gamepad layout is one table shared by all pads (directions always follow the D-pad and the left stick).
/// A corrupt or hand-edited file can never break input: unknown keys are ignored and missing ones keep their defaults.
/// </summary>
public static class InputBindings
{
    private const string Path = "user://input.cfg";

    /// <summary>The remappable fight actions, in display order.</summary>
    public static readonly (string Suffix, InputButton Button, string Label)[] Actions =
    [
        ("left", InputButton.Left, "Left"),
        ("right", InputButton.Right, "Right"),
        ("up", InputButton.Up, "Up / Jump"),
        ("down", InputButton.Down, "Down / Crouch"),
        ("light", InputButton.Light, "Light attack"),
        ("medium", InputButton.Medium, "Medium attack"),
        ("heavy", InputButton.Heavy, "Heavy attack"),
        ("special", InputButton.Special, "Special"),
        ("macro1", InputButton.Macro1, "Grab"),
        ("macro2", InputButton.Macro2, "Super"),
        ("macro3", InputButton.Macro3, "Power"),
        ("macro4", InputButton.Macro4, "Tag"),
    ];

    private static readonly Dictionary<string, JoyButton> DefaultPad = new()
    {
        ["light"] = JoyButton.A,
        ["medium"] = JoyButton.B,
        ["heavy"] = JoyButton.X,
        ["special"] = JoyButton.Y,
        ["macro1"] = JoyButton.LeftShoulder,
        ["macro2"] = JoyButton.RightShoulder,
        ["macro3"] = JoyButton.Back,
        ["macro4"] = JoyButton.RightStick,
    };

    private static readonly Dictionary<string, Key> DefaultKeys = [];
    private static readonly Dictionary<string, JoyButton> Pad = new(DefaultPad);
    private static bool _defaultsCaptured;

    /// <summary>InputMap action name of a player's fight action ("p1_light").</summary>
    public static string ActionName(int slot, string suffix) => $"p{slot + 1}_{suffix}";

    /// <summary>Gamepad button bound to a fight action, or <see cref="JoyButton.Invalid"/> when none (directions are fixed).</summary>
    public static JoyButton PadButton(string suffix) => Pad.TryGetValue(suffix, out var button) ? button : JoyButton.Invalid;

    /// <summary>Buttons of the pad layout paired with the engine input they trigger (hot path: no allocation).</summary>
    public static IReadOnlyList<(JoyButton Button, InputButton Input)> PadLayout => _padLayout;

    private static (JoyButton, InputButton)[] _padLayout = BuildPadLayout();

    private static (JoyButton, InputButton)[] BuildPadLayout()
    {
        var list = new List<(JoyButton, InputButton)>();
        foreach (var (suffix, input, _) in Actions)
        {
            if (Pad.TryGetValue(suffix, out var button) && button != JoyButton.Invalid) list.Add((button, input));
        }
        return [.. list];
    }

    /// <summary>The physical key bound to a player's action, or <see cref="Key.None"/>.</summary>
    public static Key GetKey(int slot, string suffix)
    {
        string action = ActionName(slot, suffix);
        if (!InputMap.HasAction(action)) return Key.None;
        foreach (var e in InputMap.ActionGetEvents(action))
        {
            if (e is InputEventKey key) return key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        }
        return Key.None;
    }

    /// <summary>
    /// Binds a physical key to a player's action. A key can only drive one action per player: if another action of the same
    /// player already used it, that action becomes unbound (and its name is returned so the UI can say so).
    /// </summary>
    public static string? SetKey(int slot, string suffix, Key key)
    {
        CaptureDefaults();
        string? displaced = null;
        foreach (var (other, _, label) in Actions)
        {
            if (other != suffix && GetKey(slot, other) == key)
            {
                ApplyKey(slot, other, Key.None);
                displaced = label;
            }
        }
        ApplyKey(slot, suffix, key);
        Save();
        return displaced;
    }

    public static void SetPadButton(string suffix, JoyButton button)
    {
        foreach (var other in Pad.Keys.ToArray())
        {
            if (other != suffix && Pad[other] == button) Pad[other] = JoyButton.Invalid; // one function per button
        }
        Pad[suffix] = button;
        _padLayout = BuildPadLayout();
        Save();
    }

    private static void ApplyKey(int slot, string suffix, Key key)
    {
        string action = ActionName(slot, suffix);
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionEraseEvents(action);
        if (key == Key.None) return;
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    /// <summary>Snapshot of the shipped keyboard layout (read once, before any user change).</summary>
    private static void CaptureDefaults()
    {
        if (_defaultsCaptured) return;
        _defaultsCaptured = true;
        for (int slot = 0; slot < 2; slot++)
        {
            foreach (var (suffix, _, _) in Actions) DefaultKeys[ActionName(slot, suffix)] = GetKey(slot, suffix);
        }
    }

    /// <summary>Applies the saved bindings (call once at startup, after the InputMap exists).</summary>
    public static void Load()
    {
        CaptureDefaults();
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;

        for (int slot = 0; slot < 2; slot++)
        {
            foreach (var (suffix, _, _) in Actions)
            {
                string name = ActionName(slot, suffix);
                if (!cfg.HasSectionKey("keys", name)) continue;
                long raw = (long)(cfg.GetValue("keys", name, 0L).AsInt64());
                if (raw < 0 || raw > int.MaxValue) continue; // ignore garbage
                ApplyKey(slot, suffix, (Key)(int)raw);
            }
        }

        foreach (var (suffix, _, _) in Actions)
        {
            if (!cfg.HasSectionKey("pad", suffix)) continue;
            long raw = cfg.GetValue("pad", suffix, -1L).AsInt64();
            if (raw is >= -1 and < (long)JoyButton.Max) Pad[suffix] = (JoyButton)(int)raw;
        }
        _padLayout = BuildPadLayout();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        for (int slot = 0; slot < 2; slot++)
        {
            foreach (var (suffix, _, _) in Actions) cfg.SetValue("keys", ActionName(slot, suffix), (long)GetKey(slot, suffix));
        }
        foreach (var (suffix, _, _) in Actions)
        {
            if (Pad.TryGetValue(suffix, out var button)) cfg.SetValue("pad", suffix, (long)button);
        }
        cfg.Save(Path);
    }

    /// <summary>Restores the shipped keyboard and gamepad layouts and saves them.</summary>
    public static void ResetDefaults()
    {
        CaptureDefaults();
        foreach (var (action, key) in DefaultKeys)
        {
            if (!InputMap.HasAction(action)) InputMap.AddAction(action);
            InputMap.ActionEraseEvents(action);
            if (key != Key.None) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
        Pad.Clear();
        foreach (var (suffix, button) in DefaultPad) Pad[suffix] = button;
        _padLayout = BuildPadLayout();
        Save();
    }
}
