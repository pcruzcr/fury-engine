using Fury.Game.Content;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>
/// Music that survives scene changes. It lives on the scene tree's root, crossfades between tracks and keeps playing when the
/// next scene asks for the track that is already on. Tracks are looked up by key (<c>menu</c>, <c>fight</c>, <c>training</c>,
/// <c>victory</c>) in <c>res://audio/music/</c> then <c>user://audio/music/</c> as .ogg / .mp3, or given as an explicit path (a
/// stage's own music). No file means silence, never an error.
/// </summary>
public sealed partial class Jukebox : Node
{
    private static readonly string[] Folders = ["res://audio/music", "user://audio/music"];
    private static readonly string[] Extensions = [".ogg", ".mp3"];

    private static Jukebox? _instance;

    private AudioStreamPlayer _a = null!;
    private AudioStreamPlayer _b = null!;
    private bool _onA = true;
    private string _playing = "";
    private Tween? _fade;
    private bool _ready;
    private (AudioStream? Stream, string Id, float Fade)? _pending;

    /// <summary>The key or path being played ("" when silent).</summary>
    public static string Current => _instance?._playing ?? "";

    public override void _Ready()
    {
        GameSettings.ApplyAudio();
        _a = new AudioStreamPlayer { Bus = GameSettings.MusicBus, VolumeDb = -80f };
        _b = new AudioStreamPlayer { Bus = GameSettings.MusicBus, VolumeDb = -80f };
        AddChild(_a);
        AddChild(_b);
        _ready = true;

        if (_pending is { } request)
        {
            _pending = null;
            Apply(request.Stream, request.Fade);
        }
    }

    /// <summary>
    /// Plays music for <paramref name="keyOrPath"/>: an explicit file path when it contains a '/', else a track key. When
    /// <paramref name="fallbackKey"/> is given and the first choice does not exist, that track plays instead. When no track exists
    /// at all the current music keeps playing, unless <paramref name="silenceIfMissing"/> asks for silence.
    /// </summary>
    public static void Play(Node anyNode, string keyOrPath, string? fallbackKey = null, float crossfadeSeconds = 1f, bool silenceIfMissing = false)
    {
        var jukebox = Instance(anyNode);
        var stream = Resolve(keyOrPath) ?? (fallbackKey == null ? null : Resolve(fallbackKey));
        if (stream == null && !silenceIfMissing) return;
        string id = stream == null ? "" : stream.ResourcePath.Length > 0 ? stream.ResourcePath : keyOrPath;
        if (id == jukebox._playing) return; // already on: do not restart the song on every scene change
        jukebox.Start(stream, id, crossfadeSeconds);
    }

    public static void Stop(Node anyNode, float fadeSeconds = 0.5f) => Instance(anyNode).Start(null, "", fadeSeconds);

    private static Jukebox Instance(Node anyNode)
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance)) return _instance;
        _instance = new Jukebox { Name = "Jukebox" };
        anyNode.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _instance);
        return _instance;
    }

    private void Start(AudioStream? stream, string id, float crossfade)
    {
        _playing = id;
        if (!_ready)
        {
            _pending = (stream, id, crossfade); // first use: the node joins the tree deferred and applies the latest request in _Ready
            return;
        }

        Apply(stream, crossfade);
    }

    private void Apply(AudioStream? stream, float crossfade)
    {
        var incoming = _onA ? _b : _a;
        var outgoing = _onA ? _a : _b;
        _fade?.Kill();
        _fade = CreateTween().SetParallel();

        if (stream != null)
        {
            _onA = !_onA;
            if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
            else if (stream is AudioStreamMP3 mp3) mp3.Loop = true;
            incoming.Stream = stream;
            incoming.VolumeDb = -80f;
            incoming.Play();
            _fade.TweenProperty(incoming, "volume_db", 0f, crossfade);
        }

        _fade.TweenProperty(outgoing, "volume_db", -80f, crossfade);
        _fade.Chain().TweenCallback(Callable.From(outgoing.Stop));
    }

    private static AudioStream? Resolve(string keyOrPath)
    {
        if (keyOrPath.Contains('/')) return AssetLoader.Exists(keyOrPath) ? AssetLoader.LoadAudio(keyOrPath) : null;

        foreach (string folder in Folders)
        {
            foreach (string extension in Extensions)
            {
                string path = $"{folder}/{keyOrPath}{extension}";
                if (AssetLoader.Exists(path)) return AssetLoader.LoadAudio(path);
            }
        }
        return null;
    }
}
