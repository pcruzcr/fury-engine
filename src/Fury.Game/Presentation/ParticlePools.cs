using Godot;

namespace Fury.Game.Presentation;

/// <summary>Visual category of an impact effect.</summary>
public enum ImpactKind
{
    Hit = 0,
    Block = 1,
    Ko = 2,
    Rage = 3,
}

internal static class ImpactStyle
{
    public static Color ColorOf(ImpactKind kind) => kind switch
    {
        ImpactKind.Block => new Color(0.45f, 0.8f, 1f),
        ImpactKind.Ko => new Color(1f, 0.25f, 0.2f),
        ImpactKind.Rage => new Color(1f, 0.55f, 0.1f),
        _ => new Color(1f, 0.85f, 0.35f),
    };

    public static int AmountOf(ImpactKind kind) => kind switch
    {
        ImpactKind.Ko => 40,
        ImpactKind.Rage => 28,
        ImpactKind.Block => 10,
        _ => 16,
    };

    public static Gradient FadeRamp(Color color)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, color);
        gradient.SetColor(1, new Color(color.R, color.G, color.B, 0f));
        return gradient;
    }
}

/// <summary>
/// Art overrides for the impact effects: a scene named <c>impact_hit</c>, <c>impact_block</c>, <c>impact_ko</c> or <c>impact_rage</c>
/// (<c>_3d</c> suffix for the 3D views) in <c>res://vfx/</c> (shipped content only) replaces the built-in particle burst. The scene's
/// root can be any <see cref="Node2D"/> / <see cref="Node3D"/>; on every impact all particle emitters under it restart and its
/// <c>AnimatedSprite</c> / <c>AnimationPlayer</c> (clip <c>play</c>, else the first one) start. Animated art hides itself when done.
/// </summary>
internal static class ImpactScenes
{
    // Effect scenes can carry scripts: only the game's own (res://) effects are loaded, never a mod's.
    private static readonly string[] Roots = ["res://vfx"];

    public static string KeyOf(ImpactKind kind) => kind switch
    {
        ImpactKind.Block => "impact_block",
        ImpactKind.Ko => "impact_ko",
        ImpactKind.Rage => "impact_rage",
        _ => "impact_hit",
    };

    public static PackedScene? Load(ImpactKind kind, bool threeD)
    {
        foreach (string root in Roots)
        {
            string path = $"{root}/{KeyOf(kind)}{(threeD ? "_3d" : "")}.tscn";
            if (ResourceLoader.Exists(path)) return Content.AssetLoader.LoadScene(path);
        }
        return null;
    }

    /// <summary>Hides animated art when it finishes, so an idle pooled instance never lingers on screen.</summary>
    public static void HideWhenFinished(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            switch (child)
            {
                case AnimatedSprite2D sprite:
                    sprite.AnimationFinished += () => sprite.Visible = false;
                    break;
                case AnimationPlayer player:
                    player.AnimationFinished += _ => { if (root is CanvasItem item) item.Visible = false; else if (root is Node3D n3) n3.Visible = false; };
                    break;
            }
            HideWhenFinished(child);
        }
    }

    public static void Fire(Node root)
    {
        switch (root)
        {
            case CanvasItem item: item.Visible = true; break;
            case Node3D node: node.Visible = true; break;
        }

        switch (root)
        {
            case CpuParticles2D p2: p2.Restart(); p2.Emitting = true; break;
            case CpuParticles3D p3: p3.Restart(); p3.Emitting = true; break;
            case GpuParticles2D g2: g2.Restart(); g2.Emitting = true; break;
            case GpuParticles3D g3: g3.Restart(); g3.Emitting = true; break;
            case AnimatedSprite2D sprite: sprite.Visible = true; sprite.Frame = 0; sprite.Play(); break;
            case AnimationPlayer player:
                string clip = player.HasAnimation("play") ? "play" : player.GetAnimationList().FirstOrDefault() ?? "";
                if (clip.Length > 0) player.Play(clip);
                break;
        }

        foreach (var child in root.GetChildren()) Fire(child);
    }
}


/// <summary>
/// Fixed pool of one-shot 2D particle bursts (CPU particles: identical on every renderer,
/// including the Compatibility/mobile backend). Nothing is allocated while fighting.
/// </summary>
public sealed class ParticlePool2D
{
    private const int PerKind = 6;

    private readonly Node2D[][] _pools = new Node2D[4][];
    private readonly bool[] _authored = new bool[4];
    private readonly int[] _next = new int[4];

    public ParticlePool2D(Node parent)
    {
        foreach (ImpactKind kind in System.Enum.GetValues<ImpactKind>())
        {
            var color = ImpactStyle.ColorOf(kind);
            var pool = new Node2D[PerKind];
            var authored = ImpactScenes.Load(kind, threeD: false);
            _authored[(int)kind] = authored != null;
            for (int i = 0; i < PerKind; i++)
            {
                if (authored != null && authored.Instantiate() is Node2D art)
                {
                    art.ZIndex = 20;
                    art.Visible = false;
                    ImpactScenes.HideWhenFinished(art);
                    pool[i] = art;
                    parent.AddChild(art);
                    continue;
                }

                pool[i] = new CpuParticles2D
                {
                    Emitting = false,
                    OneShot = true,
                    Amount = ImpactStyle.AmountOf(kind),
                    Lifetime = kind == ImpactKind.Ko ? 0.7 : 0.35,
                    Explosiveness = 1f,
                    Direction = Vector2.Up,
                    Spread = 180f,
                    Gravity = new Vector2(0, 700),
                    InitialVelocityMin = 140f,
                    InitialVelocityMax = kind == ImpactKind.Ko ? 520f : 340f,
                    ScaleAmountMin = 4f,
                    ScaleAmountMax = kind == ImpactKind.Ko ? 12f : 8f,
                    Color = color,
                    ColorRamp = ImpactStyle.FadeRamp(color),
                    ZIndex = 20,
                };
                parent.AddChild(pool[i]);
            }
            _pools[(int)kind] = pool;
        }
    }

    public void Burst(Vector2 position, ImpactKind kind)
    {
        var pool = _pools[(int)kind];
        int i = _next[(int)kind];
        _next[(int)kind] = (i + 1) % pool.Length;
        var p = pool[i];
        p.Position = position;
        if (_authored[(int)kind]) ImpactScenes.Fire(p);
        else if (p is CpuParticles2D particles)
        {
            particles.Restart();
            particles.Emitting = true;
        }
    }
}

/// <summary>3D counterpart of <see cref="ParticlePool2D"/>.</summary>
public sealed class ParticlePool3D
{
    private const int PerKind = 6;

    private readonly Node3D[][] _pools = new Node3D[4][];
    private readonly bool[] _authored = new bool[4];
    private readonly int[] _next = new int[4];

    public ParticlePool3D(Node parent)
    {
        foreach (ImpactKind kind in System.Enum.GetValues<ImpactKind>())
        {
            var color = ImpactStyle.ColorOf(kind);
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            var mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 3, Material = material };
            var pool = new Node3D[PerKind];
            var authored = ImpactScenes.Load(kind, threeD: true);
            _authored[(int)kind] = authored != null;
            for (int i = 0; i < PerKind; i++)
            {
                if (authored != null && authored.Instantiate() is Node3D art)
                {
                    art.Visible = false;
                    ImpactScenes.HideWhenFinished(art);
                    pool[i] = art;
                    parent.AddChild(art);
                    continue;
                }

                pool[i] = new CpuParticles3D
                {
                    Emitting = false,
                    OneShot = true,
                    Amount = ImpactStyle.AmountOf(kind),
                    Lifetime = kind == ImpactKind.Ko ? 0.7 : 0.4,
                    Explosiveness = 1f,
                    Mesh = mesh,
                    Direction = Vector3.Up,
                    Spread = 180f,
                    Gravity = new Vector3(0, -9.8f, 0),
                    InitialVelocityMin = 1.5f,
                    InitialVelocityMax = kind == ImpactKind.Ko ? 6f : 3.5f,
                    Color = color,
                    ColorRamp = ImpactStyle.FadeRamp(color),
                };
                parent.AddChild(pool[i]);
            }
            _pools[(int)kind] = pool;
        }
    }

    public void Burst(Vector3 position, ImpactKind kind)
    {
        var pool = _pools[(int)kind];
        int i = _next[(int)kind];
        _next[(int)kind] = (i + 1) % pool.Length;
        var p = pool[i];
        p.Position = position;
        if (_authored[(int)kind]) ImpactScenes.Fire(p);
        else if (p is CpuParticles3D particles)
        {
            particles.Restart();
            particles.Emitting = true;
        }
    }
}
