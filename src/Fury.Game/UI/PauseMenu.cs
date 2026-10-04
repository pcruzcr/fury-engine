using Fury.Core.Sim;
using Godot;

namespace Fury.Game.UI;

/// <summary>
/// In-fight pause overlay (offline modes). It only shows buttons: the director stops stepping the simulation while it is
/// open, so pausing can never desync anything and costs nothing.
/// </summary>
public sealed partial class PauseMenu : CanvasLayer
{
    private readonly bool _training;
    private Control _root = null!;
    private Button _resume = null!;
    private Button? _dummy;

    public PauseMenu()
        : this(false)
    {
    }

    /// <param name="training">Adds the training dummy button.</param>
    public PauseMenu(bool training) => _training = training;

    public event Action? ResumeRequested;

    public event Action? DummyRequested;

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
        if (_training) _dummy = UiKit.Button(column, Content.Localization.F("DUMMY: {0}  (F5)", "STAND"), () => DummyRequested?.Invoke());
        UiKit.Button(column, "CHANGE FIGHTER", () => CharacterSelectRequested?.Invoke());
        UiKit.Button(column, "MAIN MENU", () => MainMenuRequested?.Invoke());
    }

    public void Open()
    {
        _root.Visible = true;
        _resume.GrabFocus();
    }

    public void Close() => _root.Visible = false;

    /// <summary>Shows the dummy's current mode on its button.</summary>
    public void SetDummyLabel(DummyMode mode)
    {
        if (_dummy != null) _dummy.Text = Content.Localization.F("DUMMY: {0}  (F5)", mode.ToString().ToUpperInvariant());
    }
}
