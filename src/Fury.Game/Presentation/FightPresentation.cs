using Fury.Core.Authoring;
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
    private readonly PresentationPose[] _poseOverride = new PresentationPose[2];
    private readonly long[] _poseOverrideStart = new long[2];

    /// <summary>
    /// Makes a fighter show its intro or victory animation instead of following the simulation (cosmetic moments with no sim state).
    /// Timed with the physics tick counter, so it advances while the simulation is paused between rounds.
    /// </summary>
    public void SetPoseOverride(int fighter, PresentationPose pose)
    {
        if (_poseOverride[fighter] == pose) return;
        _poseOverride[fighter] = pose;
        _poseOverrideStart[fighter] = (long)Engine.GetPhysicsFrames();
    }

    public void ClearPoseOverrides()
    {
        SetPoseOverride(0, PresentationPose.None);
        SetPoseOverride(1, PresentationPose.None);
    }

    /// <summary>What a fighter's art should show now: the simulation's situation, or the intro / victory moment.</summary>
    protected FighterSituation SituationOf(FightSimulation sim, int fighter)
    {
        var pose = _poseOverride[fighter];
        if (pose == PresentationPose.None) return FighterSituation.From(sim, fighter);

        long physics = (long)Engine.GetPhysicsFrames();
        return new FighterSituation(
            pose == PresentationPose.Win ? AnimationStates.Win : AnimationStates.Intro,
            -1, false, false, false, (int)(physics - _poseOverrideStart[fighter]), (int)physics, 0);
    }

    /// <summary>Draw hurtboxes / hitboxes / pushboxes on top of the fighters.</summary>
    public bool ShowBoxes { get; set; }

    /// <summary>Background colour requested by the stage (null = the default look).</summary>
    public Color? StageBackground { get; set; }

    /// <summary>Layers, floor choice and music of the stage (set before the view enters the tree).</summary>
    public StageArtSet StageArt { get; set; } = StageArtSet.Default;

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

    /// <summary>The animation a fighter's art is playing (the art viewer shows it); null for the procedural fighter.</summary>
    public abstract string? CurrentAnimation(int fighter);
}

/// <summary>A cosmetic pose the director can force on a fighter.</summary>
public enum PresentationPose
{
    None = 0,
    Intro = 1,
    Win = 2,
}
