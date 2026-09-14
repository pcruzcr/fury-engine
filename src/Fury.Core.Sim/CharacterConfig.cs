using Fury.Core.Math;

namespace Fury.Core.Sim;

/// <summary>
/// Everything the sim needs to know about one fighter: stats constants plus the compiled
/// FMT1 move table (ADR-003). Built once at fight start; the move table blob is parsed and
/// then read-only.
/// </summary>
public sealed record CharacterConfig(
    string Id,
    int MaxHealth,
    Fp WalkForwardRaw,
    Fp WalkBackRaw,
    BinaryMoveTable Moves)
{
    public static CharacterConfig Create(
        string id,
        int maxHealth,
        Fp walkForward,
        Fp walkBack,
        ReadOnlySpan<MoveEntry> moves)
    {
        return new CharacterConfig(id, maxHealth, walkForward, walkBack, BinaryMoveTable.Parse(BinaryMoveTableWriter.Write(moves)));
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
