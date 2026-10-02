using System.Buffers.Binary;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>Logical sound effects of the fight. Each can be overridden by a file in <c>res://audio/sfx/</c>.</summary>
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
}

/// <summary>
/// Polyphonic audio: a pool of voices (the previous single player cut every sound with the next one),
/// music with real crossfades, and procedural placeholder sounds so the game is audible before any asset
/// exists. Drop <c>hit_light.ogg</c>, <c>block.wav</c>, ... into <c>res://audio/sfx/</c> to replace them.
/// </summary>
public sealed partial class AudioManager : Node
{
    private const int Voices = 8;
    private const int MixRate = 22050;

    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[Voices];
    private readonly Dictionary<Sfx, AudioStream> _streams = [];
    private AudioStreamPlayer _musicA = null!;
    private AudioStreamPlayer _musicB = null!;
    private bool _musicOnA = true;
    private int _nextVoice;

    public override void _Ready()
    {
        for (int i = 0; i < Voices; i++)
        {
            _voices[i] = new AudioStreamPlayer();
            AddChild(_voices[i]);
        }
        _musicA = new AudioStreamPlayer { VolumeDb = -80f };
        _musicB = new AudioStreamPlayer { VolumeDb = -80f };
        AddChild(_musicA);
        AddChild(_musicB);

        foreach (Sfx sfx in System.Enum.GetValues<Sfx>()) _streams[sfx] = LoadOrSynthesize(sfx);
        GameSettings.ApplyAudio();
    }

    /// <summary>Plays a sound on the next free voice (oldest voice is stolen when all are busy).</summary>
    public void Play(Sfx sfx, float pitch = 1f, float volumeDb = 0f)
    {
        if (!_streams.TryGetValue(sfx, out var stream)) return;
        var voice = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % Voices;
        voice.Stream = stream;
        voice.PitchScale = Mathf.Clamp(pitch, 0.25f, 4f);
        voice.VolumeDb = volumeDb;
        voice.Play();
    }

    /// <summary>Hit sound chosen by damage dealt.</summary>
    public void PlayHit(int damage) => Play(damage >= 80 ? Sfx.HitHeavy : Sfx.HitLight, pitch: damage >= 80 ? 0.9f : 1.1f);

    /// <summary>Starts music, crossfading from the current track.</summary>
    public void PlayMusic(AudioStream? stream, float crossfadeSeconds = 1f)
    {
        if (stream == null) return;
        var incoming = _musicOnA ? _musicB : _musicA;
        var outgoing = _musicOnA ? _musicA : _musicB;
        _musicOnA = !_musicOnA;

        incoming.Stream = stream;
        incoming.VolumeDb = -80f;
        incoming.Play();

        var tween = CreateTween().SetParallel();
        tween.TweenProperty(incoming, "volume_db", 0f, crossfadeSeconds);
        tween.TweenProperty(outgoing, "volume_db", -80f, crossfadeSeconds);
        tween.Chain().TweenCallback(Callable.From(outgoing.Stop));
    }

    public void StopMusic(float fadeSeconds = 0.5f)
    {
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(_musicA, "volume_db", -80f, fadeSeconds);
        tween.TweenProperty(_musicB, "volume_db", -80f, fadeSeconds);
        tween.Chain().TweenCallback(Callable.From(() => { _musicA.Stop(); _musicB.Stop(); }));
    }

    // ------------------------------------------------------------------ assets / placeholders

    private static string FileBase(Sfx sfx) => sfx switch
    {
        Sfx.HitLight => "hit_light",
        Sfx.HitHeavy => "hit_heavy",
        Sfx.Block => "block",
        Sfx.Ko => "ko",
        Sfx.Rage => "rage",
        Sfx.RoundStart => "round_start",
        Sfx.UiMove => "ui_move",
        _ => "ui_confirm",
    };

    private static AudioStream LoadOrSynthesize(Sfx sfx)
    {
        foreach (var ext in new[] { ".ogg", ".wav", ".mp3" })
        {
            string path = $"res://audio/sfx/{FileBase(sfx)}{ext}";
            if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is AudioStream loaded) return loaded;
        }

        return sfx switch
        {
            Sfx.HitLight => Tone(260f, 90f, 0.12f, noise: 0.55f),
            Sfx.HitHeavy => Tone(170f, 50f, 0.2f, noise: 0.7f),
            Sfx.Block => Tone(900f, 520f, 0.09f, noise: 0.15f),
            Sfx.Ko => Tone(420f, 40f, 0.8f, noise: 0.5f),
            Sfx.Rage => Tone(180f, 900f, 0.4f, noise: 0.1f),
            Sfx.RoundStart => Tone(440f, 660f, 0.25f, noise: 0f),
            Sfx.UiMove => Tone(700f, 700f, 0.04f, noise: 0f),
            _ => Tone(520f, 880f, 0.1f, noise: 0f),
        };
    }

    /// <summary>Procedural 16-bit mono tone with a frequency sweep, noise and an exponential decay.</summary>
    private static AudioStreamWav Tone(float freqStart, float freqEnd, float seconds, float noise)
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
            double value = ((tone * (1.0 - noise)) + (n * noise)) * envelope * 0.55;
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
