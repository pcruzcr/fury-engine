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
    public const string ControlsScene = "res://scenes/menu/controls.tscn";
    public const string OnlineScene = "res://scenes/menu/online.tscn";
    public const string ReplaysScene = "res://scenes/menu/replays.tscn";
    public const string FrameDataEditorScene = "res://scenes/tools/frame_data_editor.tscn";
    public const string StageEditorScene = "res://scenes/tools/stage_editor.tscn";
    public const string CharacterViewerScene = "res://scenes/tools/character_viewer.tscn";

    public const int DefaultNetPort = 7777;

    public static GameMode Mode { get; set; } = GameMode.Versus;
    public static string P1CharacterId { get; set; } = "glen";
    public static string P2CharacterId { get; set; } = "kira";

    // Online. The host is slot 0, the guest slot 1. Players connect directly, or through a relay when NetUseRelay is set.
    public static bool NetIsHost { get; set; } = true;
    public static string NetRemoteHost { get; set; } = "127.0.0.1";
    public static int NetPort { get; set; } = DefaultNetPort;
    public static int NetInputDelay { get; set; } = 2;
    public static bool NetUseRelay { get; set; }
    public static string NetRelayHost { get; set; } = "";
    public static int NetRelayPort { get; set; } = DefaultNetPort;
    public static string NetRoomCode { get; set; } = "";

    // Replay playback.
    public static string? ReplayPath { get; set; }

    /// <summary>Overrides the round length in seconds when greater than 0 (tests and CI; both online peers must use the same value).</summary>
    public static int RoundSecondsOverride { get; set; }

    /// <summary>The chosen fighters; an id whose content pack is no longer installed falls back to a built-in character instead of crashing.</summary>
    public static CharacterConfig CreateP1() => CharacterRoster.Create(CharacterRoster.Contains(P1CharacterId) ? P1CharacterId : "glen");

    public static CharacterConfig CreateP2() => CharacterRoster.Create(CharacterRoster.Contains(P2CharacterId) ? P2CharacterId : "kira");
}
