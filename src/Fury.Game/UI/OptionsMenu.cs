using Godot;

namespace Fury.Game.UI;

/// <summary>View mode, accessibility, audio and debug options. Every change is applied and saved immediately.</summary>
public partial class OptionsMenu : Control
{
    public override void _Ready()
    {
        var column = UiKit.CenterColumn(this, 14);
        column.CustomMinimumSize = new Vector2(520, 0);
        UiKit.Title(column, "OPTIONS", 48);

        var view = new OptionButton();
        view.AddItem("2D (flat)", (int)ViewMode.Flat2D);
        view.AddItem("2.5D (perspective)", (int)ViewMode.Perspective2_5D);
        view.AddItem("3D (cinematic camera)", (int)ViewMode.Cinematic3D);
        view.Selected = (int)GameSettings.ViewMode;
        view.ItemSelected += i =>
        {
            GameSettings.ViewMode = (ViewMode)(int)i;
            GameSettings.Save();
        };
        UiKit.Row(column, "Presentation", view);

        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = GameSettings.MasterVolume };
        volume.ValueChanged += v =>
        {
            GameSettings.MasterVolume = (float)v;
            GameSettings.ApplyAudio();
            GameSettings.Save();
        };
        UiKit.Row(column, "Master volume", volume);

        AddToggle(column, "Reduce motion (no camera shake)", GameSettings.ReduceMotion, v => GameSettings.ReduceMotion = v);
        AddToggle(column, "Reduce flashes (photosensitivity)", GameSettings.ReduceFlashes, v => GameSettings.ReduceFlashes = v);
        AddToggle(column, "Show hitboxes", GameSettings.ShowHitboxes, v => GameSettings.ShowHitboxes = v);
        AddToggle(column, "Debug overlay (F3)", GameSettings.ShowDebugOverlay, v => GameSettings.ShowDebugOverlay = v);

        UiKit.Text(column, "P1: WASD + J K L (attacks) I (special) U/O (macro)\nP2: arrows + 1 2 3 (attacks) 4 (special) 5/6 (macro)\nGamepad: D-pad / left stick, A B X Y\nIn a fight: F1 hitboxes / F2 view / F3 debug", 16);
        var back = UiKit.Button(column, "BACK", () => UiKit.GoTo(this, GameSession.MainMenuScene));
        back.GrabFocus();
    }

    private static void AddToggle(Container parent, string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Text = text, ButtonPressed = value };
        box.Toggled += pressed =>
        {
            set(pressed);
            GameSettings.Save();
        };
        parent.AddChild(box);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) UiKit.GoTo(this, GameSession.MainMenuScene);
    }
}
