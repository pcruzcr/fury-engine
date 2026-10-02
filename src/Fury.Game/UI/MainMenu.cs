using Godot;

namespace Fury.Game.UI;

/// <summary>Main menu: every game mode, options and (in debug builds) the authoring tools.</summary>
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        GameSettings.Load();
        GameSettings.ApplyAudio();

        var column = UiKit.CenterColumn(this);
        UiKit.Title(column, "DUAL FURY", 72);
        UiKit.Text(column, "Fury Engine", 20);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

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
