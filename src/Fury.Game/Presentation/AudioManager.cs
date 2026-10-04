using System.Buffers.Binary;
using Fury.Core.Authoring;
using Fury.Game.Content;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>Logical sound effects of the fight. Each has a built-in tone and can be replaced by art (see <see cref="AudioKeys"/>).</summary>
public enum Sfx
{
    HitLight = 0,
    HitHeavy = 1,
    Block = 2,
    Ko = 3,
    Rage = 4,
    RoundStart = 5,
    UiMove = 6,
    UiConfirm = 7,
    Whoosh = 8,
    Jump = 9,
    Dash = 10,
    Projectile = 11,
    Knockdown = 12,
    Win = 13,
}

/// <summary>
/// Polyphonic sound effects on the Sfx bus: a pool of voices, procedural placeholder tones so the game is audible before any
/// asset exists, and a layered lookup so art can replace any sound without code. A sound is searched in the acting character's
/// pack (<c>audio.sfx</c> in its manifest), then in <c>res://audio/sfx/</c> and <c>user://audio/sfx/</c> (<c>hit_light.ogg</c>,
/// <c>block.wav</c>...), then the built-in tone. Character voices have no built-in tone: they are silent until recorded.
/// </summary>
public sealed partial class AudioManager : Node
{
    private const int Voices = 10;
    private const int MixRate = 22050;
    private static readonly string[] SharedFolders = ["res://audio/sfx", "user://audio/sfx"];
    private static readonly string[] Extensions = [".ogg", ".wav", ".mp3"];

    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[Voices];
    private readonly Dictionary<Sfx, AudioStream> _fallbacks = [];
    private readonly Dictionary<string, AudioStream?> _resolved = [];
    private int _nextVoice;

    public override void _Ready()
    {
        GameSettings.ApplyAudio(); // creates the buses before the first voice asks for one
        for (int i = 0; i < Voices; i++)
        {
            _voices[i] = new AudioStreamPlayer { Bus = GameSettings.SfxBus };
            AddChild(_voices[i]);
        }

        foreach (Sfx sfx in System.Enum.GetValues<Sfx>()) _fallbacks[sfx] = Synthesize(sfx);
    }

    /// <summary>Plays a sound, preferring the acting character's own version, on the next free voice (the oldest is stolen when all are busy).</summary>
    public void Play(Sfx sfx, float pitch = 1f, float volumeDb = 0f, CharacterEntry? who = null)
    {
        var stream = Resolve(KeyOf(sfx), who) ?? _fallbacks[sfx];
        Start(stream, pitch, volumeDb);
    }

    /// <summary>Plays a character-only sound (a voice line or a per-move whoosh). Silent when the pack does not provide it.</summary>
    public bool PlayCharacter(CharacterEntry? who, string key, float volumeDb = 0f)
    {
        var stream = who == null ? null : Resolve(key, who);
        if (stream == null) return false;
        Start(stream, 1f, volumeDb);
        return true;
    }

    /// <summary>Hit sound chosen by damage dealt, in the attacker's own flavour when its pack has one.</summary>
    public void PlayHit(int damage, CharacterEntry? attacker = null) =>
        Play(damage >= 80 ? Sfx.HitHeavy : Sfx.HitLight, pitch: damage >= 80 ? 0.9f : 1.1f, who: attacker);

    /// <summary>Forgets resolved lookups (after the content was reloaded).</summary>
    public void ClearCache() => _resolved.Clear();

    private void Start(AudioStream stream, float pitch, float volumeDb)
    {
        var voice = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % Voices;
        voice.Stream = stream;
        voice.PitchScale = Mathf.Clamp(pitch, 0.25f, 4f);
        voice.VolumeDb = volumeDb;
        voice.Play();
    }

    // ------------------------------------------------------------------ lookup

    /// <summary>Snake-case key of a built-in sound (the one used in manifests and file names).</summary>
    public static string KeyOf(Sfx sfx) => sfx switch
    {
        Sfx.HitLight => "hit_light",
        Sfx.HitHeavy => "hit_heavy",
        Sfx.Block => "block",
        Sfx.Ko => "ko",
        Sfx.Rage => "rage",
        Sfx.RoundStart => "round_start",
        Sfx.UiMove => "ui_move",
        Sfx.UiConfirm => "ui_confirm",
        Sfx.Whoosh => "whoosh",
        Sfx.Jump => "jump",
        Sfx.Dash => "dash",
        Sfx.Projectile => "projectile",
        Sfx.Knockdown => "knockdown",
        _ => "win",
    };

    private AudioStream? Resolve(string key, CharacterEntry? who)
    {
        string cacheKey = $"{who?.Id}:{key}";
        if (_resolved.TryGetValue(cacheKey, out var cached)) return cached;

        AudioStream? stream = null;
        if (who?.Manifest != null && who.Manifest.Audio.Sfx.TryGetValue(key, out string? file) && !string.IsNullOrEmpty(file))
        {
            stream = AssetLoader.LoadAudio(who.Resolve(file));
        }

        if (stream == null && who == null)
        {
            // The shared folders only supply the plain, character-less sounds.
            foreach (string folder in SharedFolders)
            {
                foreach (string extension in Extensions)
                {
                    string path = $"{folder}/{key}{extension}";
                    if (!AssetLoader.Exists(path)) continue;
                    stream = AssetLoader.LoadAudio(path);
                    if (stream != null) break;
                }
                if (stream != null) break;
            }
        }
        else if (stream == null && who != null && !key.StartsWith("voice_", StringComparison.Ordinal) && !key.StartsWith("whoosh_", StringComparison.Ordinal))
        {
            stream = Resolve(key, null); // a character without its own version of a shared sound uses the shared one
        }

        _resolved[cacheKey] = stream;
        return stream;
    }

    // ------------------------------------------------------------------ placeholders

    private static AudioStream Synthesize(Sfx sfx) => sfx switch
    {
        Sfx.HitLight => Tone(260f, 90f, 0.12f, noise: 0.55f),
        Sfx.HitHeavy => Tone(170f, 50f, 0.2f, noise: 0.7f),
        Sfx.Block => Tone(900f, 520f, 0.09f, noise: 0.15f),
        Sfx.Ko => Tone(420f, 40f, 0.8f, noise: 0.5f),
        Sfx.Rage => Tone(180f, 900f, 0.4f, noise: 0.1f),
        Sfx.RoundStart => Tone(440f, 660f, 0.25f, noise: 0f),
        Sfx.UiMove => Tone(700f, 700f, 0.04f, noise: 0f),
        Sfx.UiConfirm => Tone(520f, 880f, 0.1f, noise: 0f),
        Sfx.Whoosh => Tone(1400f, 500f, 0.09f, noise: 0.9f, gain: 0.25f),
        Sfx.Jump => Tone(300f, 520f, 0.08f, noise: 0.1f, gain: 0.4f),
        Sfx.Dash => Tone(900f, 300f, 0.11f, noise: 0.8f, gain: 0.35f),
        Sfx.Projectile => Tone(220f, 640f, 0.22f, noise: 0.25f, gain: 0.6f),
        Sfx.Knockdown => Tone(150f, 35f, 0.3f, noise: 0.6f),
        _ => Tone(520f, 1040f, 0.5f, noise: 0f),
    };

    /// <summary>Procedural 16-bit mono tone with a frequency sweep, noise and an exponential decay.</summary>
    private static AudioStreamWav Tone(float freqStart, float freqEnd, float seconds, float noise, float gain = 0.55f)
    {
        int samples = (int)(MixRate * seconds);
        var data = new byte[samples * 2];
        var rng = new System.Random(1234);
        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)samples;
            double freq = freqStart + ((freqEnd - freqStart) * t);
            phase += 2.0 * System.Math.PI * freq / MixRate;
            double tone = System.Math.Sin(phase);
            double n = (rng.NextDouble() * 2.0) - 1.0;
            double envelope = System.Math.Exp(-4.0 * t) * System.Math.Min(1.0, i / 40.0);
            double value = ((tone * (1.0 - noise)) + (n * noise)) * envelope * gain;
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), (short)(value * short.MaxValue));
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
    }
}
