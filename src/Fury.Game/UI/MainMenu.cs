using Godot;

namespace Fury.Game.UI;

/// <summary>Main menu: every game mode, options and (in debug builds) the authoring tools.</summary>
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        GameSettings.Load();
        Content.Localization.Apply(GameSettings.Language);
        GameSettings.ApplyAudio();
        Content.UiTheme.Apply(this);
        Fury.Game.Tools.CharacterMods.LoadAll();
        InputBindings.Load();
        Presentation.Jukebox.Play(this, "menu", silenceIfMissing: true);

        if (OS.GetCmdlineUserArgs().Contains("--fury-report"))
        {
            // Export / CI self-check: what the running build found (works in exported builds, where scenes cannot be chosen).
            GD.Print($"[fury] content: {Content.ContentLibrary.Characters.Count} characters, {Content.Localization.Languages.Count} languages, {Stages.StageLoader.List().Count} stages");
            GetTree().Quit();
            return;
        }

        var column = UiKit.CenterColumn(this, 8); // compact: seven buttons plus the title fit a 540 px window without scrolling
        var logo = Content.UiTheme.Texture("logo.png");
        if (logo != null) column.AddChild(new TextureRect { Texture = logo, ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(0, 160) });
        else UiKit.Title(column, "DUAL FURY", 60);
        UiKit.Text(column, "Fury Engine", 20);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var first = UiKit.Button(column, "ARCADE", () => Start(GameMode.Arcade));
        UiKit.Button(column, "VERSUS", () => Start(GameMode.Versus));
        UiKit.Button(column, "TRAINING", () => Start(GameMode.Training));
        UiKit.Button(column, "ONLINE", () => UiKit.GoTo(this, GameSession.OnlineScene));
        UiKit.Button(column, "REPLAYS", () => UiKit.GoTo(this, GameSession.ReplaysScene));
        UiKit.Button(column, "OPTIONS", () => UiKit.GoTo(this, GameSession.OptionsScene));
        if (OS.IsDebugBuild())
        {
            UiKit.Button(column, "FRAME DATA EDITOR", () => UiKit.GoTo(this, GameSession.FrameDataEditorScene), 18);
            UiKit.Button(column, "STAGE EDITOR", () => UiKit.GoTo(this, GameSession.StageEditorScene), 18);
            UiKit.Button(column, "ART VIEWER", () => UiKit.GoTo(this, GameSession.CharacterViewerScene), 18);
        }
        if (!OS.HasFeature("web") && !OS.HasFeature("mobile")) UiKit.Button(column, "QUIT", () => GetTree().Quit());

        first.GrabFocus(); // keyboard / gamepad navigation works out of the box
    }

    private void Start(GameMode mode)
    {
        GameSession.Mode = mode;
        UiKit.GoTo(this, GameSession.CharacterSelectScene);
    }
}
