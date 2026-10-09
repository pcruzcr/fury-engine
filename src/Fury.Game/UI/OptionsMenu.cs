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

        var language = new OptionButton();
        language.AddItem("Automatic");
        language.AddItem("English");
        foreach (var table in Content.Localization.Languages) language.AddItem(table.Name);
        language.Selected = GameSettings.Language.Length == 0 ? 0 : GameSettings.Language == "en" ? 1 : 2 + Content.Localization.Languages.ToList().FindIndex(t => t.Code == GameSettings.Language);
        if (language.Selected < 0) language.Selected = 0;
        language.ItemSelected += i =>
        {
            GameSettings.Language = i == 0 ? "" : i == 1 ? "en" : Content.Localization.Languages[(int)i - 2].Code;
            Content.Localization.Apply(GameSettings.Language);
            GameSettings.Save();
        };
        UiKit.Row(column, "Language", language);

        var stages = new OptionButton();
        stages.AddItem("Default arena");
        var names = Stages.StageLoader.List();
        foreach (string name in names) stages.AddItem(name);
        int saved = names.ToList().IndexOf(GameSettings.StageName);
        stages.Selected = saved < 0 ? 0 : saved + 1;
        stages.ItemSelected += i =>
        {
            GameSettings.StageName = i == 0 ? "" : names[(int)i - 1];
            GameSettings.Save();
        };
        UiKit.Row(column, "Stage (offline modes)", stages);

        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = GameSettings.MasterVolume };
        volume.ValueChanged += v =>
        {
            GameSettings.MasterVolume = (float)v;
            GameSettings.ApplyAudio();
            GameSettings.Save();
        };
        UiKit.Row(column, "Master volume", volume);
        AddVolume(column, "Music volume", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v);
        AddVolume(column, "Effects volume", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v);

        AddToggle(column, "Fullscreen (F11)", GameSettings.Fullscreen, v => { GameSettings.Fullscreen = v; VideoSettings.Apply(this); });
        AddToggle(column, "V-Sync", GameSettings.VSync, v => { GameSettings.VSync = v; VideoSettings.Apply(this); });
        AddToggle(column, "Reduce motion (no camera shake)", GameSettings.ReduceMotion, v => GameSettings.ReduceMotion = v);
        AddToggle(column, "Reduce flashes (photosensitivity)", GameSettings.ReduceFlashes, v => GameSettings.ReduceFlashes = v);
        AddToggle(column, "Show hitboxes", GameSettings.ShowHitboxes, v => GameSettings.ShowHitboxes = v);
        AddToggle(column, "Debug overlay (F3)", GameSettings.ShowDebugOverlay, v => GameSettings.ShowDebugOverlay = v);

        UiKit.Text(column, "Every key and pad button can be remapped in CONTROLS.\nIn a fight: F1 hitboxes / F2 view / F3 debug / Esc pause", 16);
        UiKit.Button(column, "CONTROLS", () => UiKit.GoTo(this, GameSession.ControlsScene));
        UiKit.Button(column, "BACK", () => UiKit.GoTo(this, GameSession.MainMenuScene));
        view.GrabFocus(); // the first control: grabbing BACK would scroll the menu to its end before the player sees the top
    }

    private static void AddVolume(Container parent, string label, float value, Action<float> set)
    {
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value };
        slider.ValueChanged += v =>
        {
            set((float)v);
            GameSettings.ApplyAudio();
            GameSettings.Save();
        };
        UiKit.Row(parent, label, slider);
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
