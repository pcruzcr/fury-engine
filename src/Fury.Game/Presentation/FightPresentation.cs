using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>
/// Presentation of one fight. The deterministic 2D simulation is the single source of truth; a view only
/// READS it (Fp → float happens in <see cref="WorldProjection"/>) and never feeds anything back, so any
/// view can be swapped at runtime — flat 2D sprites, a perspective 2.5D scene or a cinematic 3D camera —
/// without touching gameplay, rollback or replays.
/// </summary>
public abstract partial class FightPresentation : Node
{
    /// <summary>Draw hurtboxes / hitboxes / pushboxes on top of the fighters.</summary>
    public bool ShowBoxes { get; set; }

    /// <summary>Background colour requested by the stage (null = the default look).</summary>
    public Color? StageBackground { get; set; }

    /// <summary>Colour of a stage element by behaviour (shared by every view).</summary>
    protected static Color PlatformColor(PlatformSystem.PlatformType type) => type switch
    {
        PlatformSystem.PlatformType.Hazard => new Color(0.95f, 0.2f, 0.15f, 0.55f),
        PlatformSystem.PlatformType.Bounce => new Color(0.3f, 0.85f, 0.4f),
        PlatformSystem.PlatformType.Conveyor => new Color(0.95f, 0.8f, 0.25f),
        PlatformSystem.PlatformType.PassThrough => new Color(0.45f, 0.65f, 0.95f, 0.85f),
        PlatformSystem.PlatformType.Moving => new Color(0.75f, 0.55f, 0.95f),
        _ => new Color(0.6f, 0.52f, 0.45f),
    };

    /// <summary>Attaches the simulation to display. Call again after a rematch.</summary>
    public abstract void Bind(FightSimulation sim);

    /// <summary>Copies the current simulation state to the scene. Call once per simulation tick.</summary>
    public abstract void Sync();

    /// <summary>Spawns an impact effect on <paramref name="fighter"/> (0 or 1).</summary>
    public abstract void SpawnImpact(int fighter, ImpactKind kind);

    /// <summary>Adds camera shake (0..1). Ignored when the player enabled "reduce motion".</summary>
    public abstract void Shake(float strength);
}
