using Fury.Core.Data;
using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>Combat states of one fighter. Values are stable — they live in snapshots and replays.</summary>
public static class FighterStateId
{
    public const int Idle = 0;
    public const int WalkForward = 1;
    public const int WalkBack = 2;
    public const int Crouch = 3;
    public const int Jump = 4;
    public const int Attack = 5;
    public const int Hitstun = 6;
    public const int Blockstun = 7;
    public const int Knockdown = 8;
    public const int Ko = 9;
    public const int Dash = 10;
    public const int Backdash = 11;

    /// <summary>True when the fighter may start a new action (grounded neutral states).</summary>
    public static bool IsNeutral(int state) => state is Idle or WalkForward or WalkBack or Crouch;

    /// <summary>True when the fighter can block incoming attacks this frame.</summary>
    public static bool CanBlock(int state) => state is Idle or WalkBack or Crouch or Blockstun;
}

/// <summary>Tuning constants of the fight stage and rules. All Q16.16 raws unless noted.</summary>
public sealed record FightConfig(
    int MaxHealth,
    int RoundSeconds,
    Fp StageHalfWidth,
    Fp GravityRaw,
    Fp JumpVelocityRaw,
    Fp PushboxHalfWidth,
    Fp HurtboxHalfWidth,
    Fp HurtboxHeight,
    Fp ExGainPerHit,
    Fp ResonanceGainOnBlock,
    Fp ResonanceGainOnHurt,
    int RageHealthThreshold,
    int RageDamagePercent,
    int ComboScaleStepPercent,
    int ComboScaleMinPercent,
    int ChipDamagePercent,
    int JugglePopRaw = 0x6666,
    int KnockdownPopRaw = 0x8CCC,
    int KnockdownFrames = 45,
    int WakeupInvulnFrames = 8,
    int MaxProjectilesPerFighter = 2,
    int PushFrictionPercent = 75)
{
    /// <summary>Fingerprint of every rule constant (replay and netplay compatibility).</summary>
    public ulong Fingerprint()
    {
        ReadOnlySpan<int> words =
        [
            MaxHealth, RoundSeconds, StageHalfWidth.Raw, GravityRaw.Raw, JumpVelocityRaw.Raw, PushboxHalfWidth.Raw,
            HurtboxHalfWidth.Raw, HurtboxHeight.Raw, ExGainPerHit.Raw, ResonanceGainOnBlock.Raw, ResonanceGainOnHurt.Raw,
            RageHealthThreshold, RageDamagePercent, ComboScaleStepPercent, ComboScaleMinPercent, ChipDamagePercent,
            JugglePopRaw, KnockdownPopRaw, KnockdownFrames, WakeupInvulnFrames, MaxProjectilesPerFighter, PushFrictionPercent,
        ];
        return FpHash.HashInts(words);
    }

    public static FightConfig Default() => new(
        MaxHealth: 1000,
        RoundSeconds: 99,
        StageHalfWidth: Fp.FromRatio(24, 1),
        GravityRaw: Fp.FromRatio(1, 25),
        JumpVelocityRaw: Fp.FromRatio(7, 10),
        PushboxHalfWidth: Fp.FromRatio(1, 1),
        HurtboxHalfWidth: Fp.FromRatio(1, 2),
        HurtboxHeight: Fp.FromRatio(4, 1),
        ExGainPerHit: Fp.FromRatio(12, 1),
        ResonanceGainOnBlock: Fp.FromRatio(8, 1),
        ResonanceGainOnHurt: Fp.FromRatio(5, 1),
        RageHealthThreshold: 300,
        RageDamagePercent: 115,
        ComboScaleStepPercent: 10,
        ComboScaleMinPercent: 60,
        ChipDamagePercent: 10);
}
