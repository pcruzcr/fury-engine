using Godot;

namespace Fury.Game;

/// <summary>How the deterministic 2D simulation is shown. The simulation is identical in all modes.</summary>
public enum ViewMode
{
    /// <summary>Orthographic 2D side view (classic sprites).</summary>
    Flat2D = 0,

    /// <summary>3D scene, side-on perspective camera locked to the fight plane (Tekken / Street Fighter 6 style).</summary>
    Perspective2_5D = 1,

    /// <summary>3D scene with a cinematic orbiting camera (replays, training, showcase).</summary>
    Cinematic3D = 2,
}

/// <summary>
/// Player-facing settings, persisted in <c>user://settings.cfg</c>. Accessibility options default to
/// the comfortable side; every value is validated on load so a hand-edited file cannot crash the game.
/// </summary>
public static class GameSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "game";

    public static ViewMode ViewMode { get; set; } = ViewMode.Flat2D;

    /// <summary>Disables camera shake and zoom punches (motion-sensitivity option).</summary>
    public static bool ReduceMotion { get; set; }

    /// <summary>Disables full-screen flashes and bright impact bursts (photosensitivity option).</summary>
    public static bool ReduceFlashes { get; set; }

    public static bool ShowDebugOverlay { get; set; }

    public static bool ShowHitboxes { get; set; }

    /// <summary>Master volume, linear 0..1.</summary>
    public static float MasterVolume { get; set; } = 0.8f;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;

        int mode = (int)cfg.GetValue(Section, "view_mode", (int)ViewMode);
        ViewMode = System.Enum.IsDefined(typeof(ViewMode), mode) ? (ViewMode)mode : ViewMode.Flat2D;
        ReduceMotion = (bool)cfg.GetValue(Section, "reduce_motion", ReduceMotion);
        ReduceFlashes = (bool)cfg.GetValue(Section, "reduce_flashes", ReduceFlashes);
        ShowDebugOverlay = (bool)cfg.GetValue(Section, "debug_overlay", ShowDebugOverlay);
        ShowHitboxes = (bool)cfg.GetValue(Section, "hitboxes", ShowHitboxes);
        MasterVolume = Mathf.Clamp((float)cfg.GetValue(Section, "master_volume", MasterVolume), 0f, 1f);
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "view_mode", (int)ViewMode);
        cfg.SetValue(Section, "reduce_motion", ReduceMotion);
        cfg.SetValue(Section, "reduce_flashes", ReduceFlashes);
        cfg.SetValue(Section, "debug_overlay", ShowDebugOverlay);
        cfg.SetValue(Section, "hitboxes", ShowHitboxes);
        cfg.SetValue(Section, "master_volume", MasterVolume);
        cfg.Save(Path);
    }

    /// <summary>Applies the audio setting to the engine's master bus.</summary>
    public static void ApplyAudio()
    {
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(MasterVolume, 0.0001f)));
    }
}
