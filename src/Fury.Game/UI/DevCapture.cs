using Godot;

namespace Fury.Game.UI;

/// <summary>
/// Dev / CI hook for the menu scenes: with <c>--fury-capture=file.png</c> (and optionally <c>--fury-frames=n</c>, default 20) the
/// scene saves a screenshot after n rendered frames and quits. Used to check how a menu looks in another language or with a theme.
/// </summary>
public partial class DevCapture : Node
{
    private string _path = "";
    private int _after = 20;
    private int _frames;

    /// <summary>Adds the hook to <paramref name="host"/> when the command line asks for a capture.</summary>
    public static void AttachIfRequested(Node host)
    {
        string? path = null;
        int after = 20;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--fury-capture=", StringComparison.Ordinal)) path = arg[15..];
            else if (arg.StartsWith("--fury-frames=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(14), out int n) && n > 0) after = n;
        }

        if (path != null) host.AddChild(new DevCapture { _path = path, _after = after });
    }

    public override void _Process(double delta)
    {
        if (++_frames < _after) return;
        var error = GetViewport().GetTexture().GetImage().SavePng(_path);
        GD.Print(error == Error.Ok ? $"[fury] captured {_path}" : $"[fury] capture failed: {error}");
        GetTree().Quit();
    }
}
