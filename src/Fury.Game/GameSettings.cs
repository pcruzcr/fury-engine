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

    /// <summary>Show the on-screen touch pad even without a touch screen (testing; <c>--fury-touch</c>).</summary>
    public static bool ForceTouchControls { get; set; }

    /// <summary>Saved stage to fight on in the offline modes (a name from <c>user://stages</c>); empty = the default arena.</summary>
    public static string StageName { get; set; } = "";

    /// <summary>Master volume, linear 0..1.</summary>
    public static float MasterVolume { get; set; } = 0.8f;

    /// <summary>Borderless fullscreen (F11 toggles it from anywhere).</summary>
    public static bool Fullscreen { get; set; }

    /// <summary>Vertical sync: smoother motion, a little more input latency on slow displays.</summary>
    public static bool VSync { get; set; } = true;

    /// <summary>Interface language: a locale code from <c>i18n/</c>, <c>"en"</c>, or empty to follow the system.</summary>
    public static string Language { get; set; } = "";

    /// <summary>Music volume (the Music bus), linear 0..1.</summary>
    public static float MusicVolume { get; set; } = 0.7f;

    /// <summary>Sound effects and voices volume (the Sfx bus), linear 0..1.</summary>
    public static float SfxVolume { get; set; } = 1f;

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
        StageName = cfg.GetValue(Section, "stage", "").AsString();
        MasterVolume = Mathf.Clamp((float)cfg.GetValue(Section, "master_volume", MasterVolume), 0f, 1f);
        Language = cfg.GetValue(Section, "language", "").AsString();
        Fullscreen = (bool)cfg.GetValue(Section, "fullscreen", Fullscreen);
        VSync = (bool)cfg.GetValue(Section, "vsync", VSync);
        MusicVolume = Mathf.Clamp((float)cfg.GetValue(Section, "music_volume", MusicVolume), 0f, 1f);
        SfxVolume = Mathf.Clamp((float)cfg.GetValue(Section, "sfx_volume", SfxVolume), 0f, 1f);
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "view_mode", (int)ViewMode);
        cfg.SetValue(Section, "reduce_motion", ReduceMotion);
        cfg.SetValue(Section, "reduce_flashes", ReduceFlashes);
        cfg.SetValue(Section, "debug_overlay", ShowDebugOverlay);
        cfg.SetValue(Section, "hitboxes", ShowHitboxes);
        cfg.SetValue(Section, "stage", StageName);
        cfg.SetValue(Section, "master_volume", MasterVolume);
        cfg.SetValue(Section, "language", Language);
        cfg.SetValue(Section, "fullscreen", Fullscreen);
        cfg.SetValue(Section, "vsync", VSync);
        cfg.SetValue(Section, "music_volume", MusicVolume);
        cfg.SetValue(Section, "sfx_volume", SfxVolume);
        cfg.Save(Path);
    }

    public const string MusicBus = "Music";
    public const string SfxBus = "Sfx";

    /// <summary>Creates the Music and Sfx buses on first use and applies the three volumes (master, music, effects).</summary>
    public static void ApplyAudio()
    {
        EnsureBus(MusicBus);
        EnsureBus(SfxBus);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(MasterVolume, 0.0001f)));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(MusicBus), Mathf.LinearToDb(Mathf.Max(MusicVolume, 0.0001f)));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(SfxBus), Mathf.LinearToDb(Mathf.Max(SfxVolume, 0.0001f)));
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        AudioServer.AddBus();
        int index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
    }
}
