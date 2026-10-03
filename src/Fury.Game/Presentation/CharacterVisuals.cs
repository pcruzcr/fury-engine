using Fury.Core.Authoring;
using Fury.Core.Sim;
using Fury.Game.Content;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>What the animation choice depends on, read from the simulation once per tick.</summary>
/// <param name="AttackTicks">Length in ticks of the current move while attacking (the animation is stretched over it), else 0.</param>
public readonly record struct FighterSituation(
    int StateId, int MoveIndex, bool Crouching, bool Airborne, bool Rising, int StateFrame, int Clock, int AttackTicks)
{
    public static FighterSituation From(FightSimulation sim, int f)
    {
        var s = sim.State;
        int state = s.GetStateId(f);
        int move = state == FighterStateId.Attack ? s.GetMoveIndex(f) : -1;
        int span = move >= 0 ? sim.Data(f).Moves.GetMove(move).TotalFrames : 0;
        return new FighterSituation(state, move, s.IsCrouching(f), s.IsAirborne(f), s.GetVy(f).Raw < 0, s.GetStateFrame(f), s.Frame, span);
    }
}

/// <summary>
/// A character's own art standing in for the procedural fighter. Implementations only read the simulation (through
/// <see cref="FighterSituation"/>) and never write back, so art can never affect gameplay, replays or rollback.
/// </summary>
public interface ICharacterVisual
{
    Node Root { get; }

    void Apply(in FighterSituation situation);
}

/// <summary>Builds the visual for a character from its pack: authored scene, then sprite strips, then the legacy per-id scene, else null (procedural fighter).</summary>
public static class CharacterVisuals
{
    public static ICharacterVisual? Create2D(CharacterEntry? entry)
    {
        var visual = entry?.Manifest?.Visual;
        if (entry != null && visual is { Scene2D.Length: > 0 })
        {
            var scene = TryScene(entry, visual.Scene2D);
            if (scene != null) return SceneVisual2D.TryCreate(scene, entry, visual.Scale);
        }

        if (entry != null && visual?.Sprites != null)
        {
            var sprites = SpriteVisual2D.TryCreate(entry, visual);
            if (sprites != null) return sprites;
        }

        var legacy = LegacyScene(entry, "visual_2d.tscn");
        return legacy == null ? null : SceneVisual2D.TryCreate(legacy, entry, 1.0);
    }

    public static ICharacterVisual? Create3D(CharacterEntry? entry)
    {
        var visual = entry?.Manifest?.Visual;
        if (entry != null && visual is { Scene3D.Length: > 0 })
        {
            var scene = TryScene(entry, visual.Scene3D);
            if (scene != null) return SceneVisual3D.TryCreate(scene, entry, visual.Scale);
        }

        if (entry != null && visual?.Sprites != null)
        {
            var sprites = SpriteVisual3D.TryCreate(entry, visual);
            if (sprites != null) return sprites;
        }

        var legacy = LegacyScene(entry, "visual_3d.tscn");
        return legacy == null ? null : SceneVisual3D.TryCreate(legacy, entry, 1.0);
    }

    private static PackedScene? TryScene(CharacterEntry entry, string relative) => AssetLoader.LoadScene(entry.Resolve(relative));

    /// <summary>Convention of the first version: <c>res://characters/&lt;id&gt;/visual_2d.tscn</c> next to no manifest.</summary>
    private static PackedScene? LegacyScene(CharacterEntry? entry, string file)
    {
        if (entry == null) return null;
        string path = $"{ContentLibrary.ShippedRoot}/{entry.Id}/{file}";
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
    }

    /// <summary>Finds the first <see cref="AnimationPlayer"/> in the instanced art (the conventional child name first).</summary>
    internal static AnimationPlayer? FindPlayer(Node root)
    {
        var named = root.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (named != null) return named;
        foreach (var child in root.GetChildren())
        {
            var found = child as AnimationPlayer ?? FindPlayer(child);
            if (found != null) return found;
        }
        return null;
    }
}

/// <summary>Plays authored <c>AnimationPlayer</c> clips by seeking to a simulation-derived time, never by playing in real time.</summary>
internal sealed class AnimationPlayerDriver
{
    private readonly AnimationPlayer _player;
    private readonly AnimationResolver _resolver;
    private string? _current;
    private Animation? _animation;

    public AnimationPlayerDriver(AnimationPlayer player, IReadOnlyList<string>? moveNames)
    {
        _player = player;
        _resolver = new AnimationResolver(moveNames, name => player.HasAnimation(name));
        _player.SpeedScale = 0f; // frozen: the simulation, not the clock, decides what is shown
    }

    public void Apply(in FighterSituation s)
    {
        string? name = _resolver.Resolve(s.StateId, s.MoveIndex, s.Crouching, s.Airborne, s.Rising);
        if (name == null) return;

        if (name != _current)
        {
            _current = name;
            _animation = _player.GetAnimation(name);
            _player.Play(name);
            _player.Pause();
        }

        if (_animation == null) return;
        bool loop = _animation.LoopMode != Animation.LoopModeEnum.None;
        _player.Seek(AnimationTiming.Time(_animation.Length, loop, s.StateFrame, s.Clock, s.AttackTicks), update: true);
    }
}

/// <summary>2D character built from an authored scene with an <c>AnimationPlayer</c>.</summary>
public sealed partial class SceneVisual2D : Node2D, ICharacterVisual
{
    private AnimationPlayerDriver _driver = null!;

    public Node Root => this;

    internal static SceneVisual2D? TryCreate(PackedScene scene, CharacterEntry? entry, double scale)
    {
        var instance = scene.Instantiate();
        var player = CharacterVisuals.FindPlayer(instance);
        if (player == null)
        {
            GD.PushWarning($"{entry?.Id}: the 2D scene has no AnimationPlayer; using the procedural fighter.");
            instance.QueueFree();
            return null;
        }

        var visual = new SceneVisual2D { Scale = new Vector2((float)scale, (float)scale) };
        visual.AddChild(instance);
        visual._driver = new AnimationPlayerDriver(player, entry?.Manifest?.MoveNames);
        return visual;
    }

    public void Apply(in FighterSituation situation) => _driver.Apply(situation);
}

/// <summary>3D character built from an authored scene (a glTF import or a hand-built scene) with an <c>AnimationPlayer</c>.</summary>
public sealed partial class SceneVisual3D : Node3D, ICharacterVisual
{
    private AnimationPlayerDriver _driver = null!;

    public Node Root => this;

    internal static SceneVisual3D? TryCreate(PackedScene scene, CharacterEntry? entry, double scale)
    {
        var instance = scene.Instantiate();
        var player = CharacterVisuals.FindPlayer(instance);
        if (player == null)
        {
            GD.PushWarning($"{entry?.Id}: the 3D scene has no AnimationPlayer; using the procedural fighter.");
            instance.QueueFree();
            return null;
        }

        var visual = new SceneVisual3D { Scale = Vector3.One * (float)scale };
        visual.AddChild(instance);
        visual._driver = new AnimationPlayerDriver(player, entry?.Manifest?.MoveNames);
        return visual;
    }

    public void Apply(in FighterSituation situation) => _driver.Apply(situation);
}

/// <summary>One animation of a sprite sheet: a strip texture with its frame count and speed.</summary>
internal sealed record SpriteStrip(Texture2D Texture, int Frames, float Fps, bool Loop);

/// <summary>Shared by the 2D and 3D sprite visuals: loads the strips and picks the strip and frame for a situation.</summary>
internal sealed class SpriteStrips
{
    private readonly Dictionary<string, SpriteStrip> _strips = new(StringComparer.Ordinal);
    private readonly AnimationResolver _resolver;
    private string? _current;

    public SpriteStrips(CharacterEntry entry, SpriteSheetDef sheet)
    {
        foreach (var (name, def) in sheet.Animations)
        {
            var texture = AssetLoader.LoadTexture(entry.Resolve(def.File));
            if (texture == null) continue;

            // Trust the image over the manifest when they disagree: the strip is what the artist actually drew.
            int frames = def.Frames;
            int fromWidth = texture.GetWidth() / System.Math.Max(1, sheet.FrameWidth);
            if (fromWidth >= 1 && fromWidth != frames)
            {
                GD.PushWarning($"{entry.Id}/{name}: manifest says {frames} frames but the image is {texture.GetWidth()} px wide ({fromWidth} of {sheet.FrameWidth} px); using {fromWidth}.");
                frames = fromWidth;
            }

            _strips[name] = new SpriteStrip(texture, System.Math.Max(1, frames), (float)def.Fps, def.Loop ?? AnimationNames.DefaultLoop(name));
        }

        _resolver = new AnimationResolver(entry.Manifest?.MoveNames, _strips.ContainsKey);
    }

    public bool IsUsable => _strips.ContainsKey(AnimationNames.Idle) || _strips.Count > 0;

    /// <summary>The strip to show and its frame; false when the character has no usable animation.</summary>
    public bool Select(in FighterSituation s, out SpriteStrip strip, out int frame, out bool changedStrip)
    {
        strip = null!;
        frame = 0;
        changedStrip = false;

        string? name = _resolver.Resolve(s.StateId, s.MoveIndex, s.Crouching, s.Airborne, s.Rising);
        if (name == null || !_strips.TryGetValue(name, out var found)) return false;

        strip = found;
        changedStrip = name != _current;
        _current = name;
        frame = AnimationTiming.FrameIndex(found.Frames, found.Fps, found.Loop, s.StateFrame, s.Clock, s.AttackTicks);
        return true;
    }
}

/// <summary>2D character drawn from sprite strips.</summary>
public sealed partial class SpriteVisual2D : Node2D, ICharacterVisual
{
    private SpriteStrips _strips = null!;
    private Sprite2D _sprite = null!;

    public Node Root => this;

    internal static SpriteVisual2D? TryCreate(CharacterEntry entry, CharacterVisual visual)
    {
        var sheet = visual.Sprites!;
        var strips = new SpriteStrips(entry, sheet);
        if (!strips.IsUsable)
        {
            GD.PushWarning($"{entry.Id}: no sprite strip could be loaded; using the procedural fighter.");
            return null;
        }

        float scale = (float)visual.Scale;
        var node = new SpriteVisual2D { Scale = new Vector2(scale, scale), _strips = strips };
        node._sprite = new Sprite2D
        {
            Centered = false,
            Offset = new Vector2(-(float)sheet.PivotX, -(float)sheet.PivotY),
            TextureFilter = visual.PixelArt ? CanvasItem.TextureFilterEnum.Nearest : CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        node.AddChild(node._sprite);
        return node;
    }

    public void Apply(in FighterSituation situation)
    {
        if (!_strips.Select(situation, out var strip, out int frame, out bool changed)) return;
        if (changed)
        {
            _sprite.Texture = strip.Texture;
            _sprite.Hframes = strip.Frames;
        }
        if (changed || _sprite.Frame != frame) _sprite.Frame = frame;
    }
}

/// <summary>Sprite strips as a flat card standing on the fight plane of the 2.5D / 3D views.</summary>
public sealed partial class SpriteVisual3D : Node3D, ICharacterVisual
{
    private SpriteStrips _strips = null!;
    private Sprite3D _sprite = null!;

    public Node Root => this;

    internal static SpriteVisual3D? TryCreate(CharacterEntry entry, CharacterVisual visual)
    {
        var sheet = visual.Sprites!;
        var strips = new SpriteStrips(entry, sheet);
        if (!strips.IsUsable) return null;

        var node = new SpriteVisual3D { _strips = strips };
        node._sprite = new Sprite3D
        {
            Centered = false,
            // Uncentered, a Sprite3D grows up and to the right from its offset (pixels, Y up): the bottom-left corner sits at the
            // offset, so the pivot pixel (PivotY rows from the top) lands on the fighter's floor position when the offset is
            // (-PivotX, PivotY - FrameHeight).
            Offset = new Vector2(-(float)sheet.PivotX, (float)(sheet.PivotY - sheet.FrameHeight)),
            PixelSize = WorldProjection.MetersPerUnit / WorldProjection.PixelsPerUnit * (float)visual.Scale,
            Shaded = false,
            DoubleSided = true,
            Transparent = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = visual.PixelArt ? BaseMaterial3D.TextureFilterEnum.Nearest : BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        node.AddChild(node._sprite);
        return node;
    }

    public void Apply(in FighterSituation situation)
    {
        if (!_strips.Select(situation, out var strip, out int frame, out bool changed)) return;
        if (changed)
        {
            _sprite.Texture = strip.Texture;
            _sprite.Hframes = strip.Frames;
        }
        if (changed || _sprite.Frame != frame) _sprite.Frame = frame;
    }
}
