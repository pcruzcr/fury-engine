using Fury.Core.Data;

namespace Fury.Game;

/// <summary>Game mode chosen in the main menu.</summary>
public enum GameMode
{
    Arcade = 0,
    Versus = 1,
    Training = 2,
    Online = 3,
    Replay = 4,
}

/// <summary>
/// Choices that travel between scenes (menu → character select → fight). Scene changes in
/// Godot destroy the previous tree, so the selection has to live outside of it.
/// </summary>
public static class GameSession
{
    public const string MainMenuScene = "res://scenes/menu/main_menu.tscn";
    public const string CharacterSelectScene = "res://scenes/select/character_select.tscn";
    public const string FightScene = "res://scenes/game/fight.tscn";
    public const string OptionsScene = "res://scenes/menu/options.tscn";
    public const string OnlineScene = "res://scenes/menu/online.tscn";
    public const string ReplaysScene = "res://scenes/menu/replays.tscn";
    public const string FrameDataEditorScene = "res://scenes/tools/frame_data_editor.tscn";
    public const string StageEditorScene = "res://scenes/tools/stage_editor.tscn";

    public const int DefaultNetPort = 7777;

    public static GameMode Mode { get; set; } = GameMode.Versus;
    public static string P1CharacterId { get; set; } = "glen";
    public static string P2CharacterId { get; set; } = "kira";

    // Online (direct connect). The host is slot 0, the guest slot 1.
    public static bool NetIsHost { get; set; } = true;
    public static string NetRemoteHost { get; set; } = "127.0.0.1";
    public static int NetPort { get; set; } = DefaultNetPort;
    public static int NetInputDelay { get; set; } = 2;

    // Replay playback.
    public static string? ReplayPath { get; set; }

    /// <summary>Overrides the round length in seconds when greater than 0 (tests and CI; both online peers must use the same value).</summary>
    public static int RoundSecondsOverride { get; set; }

    public static CharacterConfig CreateP1() => CharacterRoster.Create(P1CharacterId);

    public static CharacterConfig CreateP2() => CharacterRoster.Create(P2CharacterId);
}
