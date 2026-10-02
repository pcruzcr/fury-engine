using Godot;

namespace Fury.Game.UI;

/// <summary>
/// In-fight pause overlay (offline modes). It only shows buttons: the director stops stepping the simulation while it is
/// open, so pausing can never desync anything and costs nothing.
/// </summary>
public sealed partial class PauseMenu : CanvasLayer
{
    private Control _root = null!;
    private Button _resume = null!;

    public event Action? ResumeRequested;

    public event Action? RestartRequested;

    public event Action? CharacterSelectRequested;

    public event Action? MainMenuRequested;

    public bool IsOpen => _root.Visible;

    public override void _Ready()
    {
        Layer = 30;
        _root = new Control { Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.65f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(column);

        UiKit.Title(column, "PAUSED", 56);
        _resume = UiKit.Button(column, "RESUME", () => ResumeRequested?.Invoke());
        UiKit.Button(column, "RESTART", () => RestartRequested?.Invoke());
        UiKit.Button(column, "CHANGE FIGHTER", () => CharacterSelectRequested?.Invoke());
        UiKit.Button(column, "MAIN MENU", () => MainMenuRequested?.Invoke());
    }

    public void Open()
    {
        _root.Visible = true;
        _resume.GrabFocus();
    }

    public void Close() => _root.Visible = false;
}
