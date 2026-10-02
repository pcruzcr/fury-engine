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

    /// <summary>Attaches the simulation to display. Call again after a rematch.</summary>
    public abstract void Bind(FightSimulation sim);

    /// <summary>Copies the current simulation state to the scene. Call once per simulation tick.</summary>
    public abstract void Sync();

    /// <summary>Spawns an impact effect on <paramref name="fighter"/> (0 or 1).</summary>
    public abstract void SpawnImpact(int fighter, ImpactKind kind);

    /// <summary>Adds camera shake (0..1). Ignored when the player enabled "reduce motion".</summary>
    public abstract void Shake(float strength);

    /// <summary>Animation / pose name for a fighter state id (used to drive optional character scenes).</summary>
    public static string StateAnimation(int stateId, bool crouching) => stateId switch
    {
        FighterStateId.WalkForward => "walk_forward",
        FighterStateId.WalkBack => "walk_back",
        FighterStateId.Crouch => "crouch",
        FighterStateId.Jump => "jump",
        FighterStateId.Attack => crouching ? "attack_crouch" : "attack",
        FighterStateId.Hitstun => "hit",
        FighterStateId.Blockstun => "block",
        FighterStateId.Knockdown => "knockdown",
        FighterStateId.Ko => "ko",
        _ => "idle",
    };

    /// <summary>Plays <paramref name="animation"/> on the first AnimationPlayer under <paramref name="root"/>, if it has it.</summary>
    protected static void PlayAnimation(Node? root, string animation)
    {
        if (root == null) return;
        var player = root.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (player == null || !player.HasAnimation(animation) || player.CurrentAnimation == animation) return;
        player.Play(animation);
    }

    /// <summary>Loads <c>res://characters/{id}/{file}</c> when the project ships art for that character.</summary>
    protected static Node? TryInstanceCharacterScene(string characterId, string file)
    {
        string path = $"res://characters/{characterId}/{file}";
        return ResourceLoader.Exists(path) && ResourceLoader.Load(path) is PackedScene scene ? scene.Instantiate() : null;
    }
}
