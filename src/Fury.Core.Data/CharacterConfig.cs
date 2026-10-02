using Fury.Core.Math;

namespace Fury.Core.Data;

/// <summary>
/// Everything the sim needs to know about one fighter: stats constants plus the compiled
/// frame-data table (ADR-003). Built once at fight start; the move table is parsed and then read-only.
/// Movement stats beyond the walk speed are init-only and default to 0, which the sim resolves to
/// sensible multiples of the walk speed (see the <c>Effective*</c> members), so a character only
/// authors the stats it wants to change.
/// </summary>
public sealed record CharacterConfig(
    string Id,
    int MaxHealth,
    Fp WalkForwardRaw,
    BinaryMoveTable Moves)
{
    /// <summary>Backward walk speed. 0 = same as forward (legacy behaviour).</summary>
    public Fp WalkBackSpeed { get; init; }

    /// <summary>Forward dash speed per frame. 0 = twice the walk speed.</summary>
    public Fp DashSpeed { get; init; }

    /// <summary>Forward dash duration. 0 = 14 frames.</summary>
    public int DashFrames { get; init; }

    /// <summary>Backdash speed per frame. 0 = 1.5 times the walk speed.</summary>
    public Fp BackdashSpeed { get; init; }

    /// <summary>Backdash duration. 0 = 16 frames.</summary>
    public int BackdashFrames { get; init; }

    /// <summary>Frames of invulnerability at the start of a backdash. 0 = 6 frames.</summary>
    public int BackdashInvulnFrames { get; init; }

    public Fp EffectiveWalkBack => WalkBackSpeed.Raw != 0 ? WalkBackSpeed : WalkForwardRaw;

    public Fp EffectiveDashSpeed => DashSpeed.Raw != 0 ? DashSpeed : WalkForwardRaw * Fp.FromInt(2);

    public int EffectiveDashFrames => DashFrames > 0 ? DashFrames : 14;

    public Fp EffectiveBackdashSpeed => BackdashSpeed.Raw != 0 ? BackdashSpeed : WalkForwardRaw * Fp.FromRatio(3, 2);

    public int EffectiveBackdashFrames => BackdashFrames > 0 ? BackdashFrames : 16;

    public int EffectiveBackdashInvuln => BackdashInvulnFrames > 0 ? BackdashInvulnFrames : 6;

    /// <summary>
    /// Fingerprint of everything the simulation reads from this character. Two peers (or a replay and the
    /// current build) only agree on a fight when their fingerprints match.
    /// </summary>
    public ulong Fingerprint()
    {
        Span<int> head =
        [
            FpHash.HashName(Id), MaxHealth, WalkForwardRaw.Raw, Moves.MoveCount,
            WalkBackSpeed.Raw, DashSpeed.Raw, DashFrames, BackdashSpeed.Raw, BackdashFrames, BackdashInvulnFrames,
        ];
        return FpHash.Combine(FpHash.HashInts(head), Moves.ContentHash);
    }

    public static CharacterConfig Create(
        string id,
        int maxHealth,
        Fp walkForward,
        ReadOnlySpan<MoveEntry> moves)
    {
        return new CharacterConfig(id, maxHealth, walkForward, BinaryMoveTable.Parse(BinaryMoveTableWriter.Write(moves)));
    }
}

/// <summary>Canonical move indexes into the character tables (same layout for every character).</summary>
public static class MoveSlot
{
    public const int StandLight = 0;
    public const int StandMedium = 1;
    public const int StandHeavy = 2;
    public const int CrouchLight = 3;
    public const int CrouchHeavy = 4;
    public const int Special = 5;
    public const int Super = 6;
    public const int Count = 7;
}

/// <summary>Conventional move categories used by cancel rules (<see cref="MoveEntry.Category"/> / <see cref="MoveEntry.CancelMask"/>).</summary>
public static class MoveCategory
{
    public const int Normal = 0;
    public const int CommandNormal = 1;
    public const int Special = 2;
    public const int Super = 3;
    public const int Throw = 4;

    public static int Bit(int category) => 1 << category;
}
