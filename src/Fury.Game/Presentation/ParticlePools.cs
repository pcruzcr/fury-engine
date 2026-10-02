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
/// Fixed pool of one-shot 2D particle bursts (CPU particles: identical on every renderer,
/// including the Compatibility/mobile backend). Nothing is allocated while fighting.
/// </summary>
public sealed class ParticlePool2D
{
    private const int PerKind = 6;

    private readonly CpuParticles2D[][] _pools = new CpuParticles2D[4][];
    private readonly int[] _next = new int[4];

    public ParticlePool2D(Node parent)
    {
        foreach (ImpactKind kind in System.Enum.GetValues<ImpactKind>())
        {
            var color = ImpactStyle.ColorOf(kind);
            var pool = new CpuParticles2D[PerKind];
            for (int i = 0; i < PerKind; i++)
            {
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
        p.Restart();
        p.Emitting = true;
    }
}

/// <summary>3D counterpart of <see cref="ParticlePool2D"/>.</summary>
public sealed class ParticlePool3D
{
    private const int PerKind = 6;

    private readonly CpuParticles3D[][] _pools = new CpuParticles3D[4][];
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
            var pool = new CpuParticles3D[PerKind];
            for (int i = 0; i < PerKind; i++)
            {
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
        p.Restart();
        p.Emitting = true;
    }
}
