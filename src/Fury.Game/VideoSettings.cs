using Godot;

namespace Fury.Game;

/// <summary>
/// Window and display options that a tester needs from the first build: fullscreen (F11 anywhere) and V-Sync. Persisted by
/// <see cref="GameSettings"/>; applied once when a scene first needs them and again when the player changes them.
/// </summary>
public partial class VideoSettings : Node
{
    private static VideoSettings? _instance;

    /// <summary>Applies the saved options and installs the F11 hotkey on the scene tree's root (once).</summary>
    public static void Apply(Node anyNode)
    {
        if (_instance == null || !GodotObject.IsInstanceValid(_instance))
        {
            _instance = new VideoSettings { Name = "VideoSettings" };
            anyNode.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _instance);
        }

        DisplayServer.WindowSetMode(GameSettings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(GameSettings.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 }) return;
        GameSettings.Fullscreen = !GameSettings.Fullscreen;
        GameSettings.Save();
        Apply(this);
        GetViewport().SetInputAsHandled();
    }
}
