using Fury.Core.Math;

namespace Fury.Core.Data;

/// <summary>
/// Compiled-data contract for a character. Frame data lives in code (byte-stable JSON export
/// for tooling). All values Fp/int — authored via [CAL] calibration constants only.
/// </summary>
public interface ICharacterData
{
    string Id { get; }
    int MaxHealth { get; }
    Fp WalkSpeed { get; }
    Fp BackwalkSpeed { get; }
    Fp JumpArc { get; }
    IReadOnlyList<MoveData> Moves { get; }
}

/// <summary>One move: startup/active/recovery plus hitboxes per active frame.</summary>
public sealed record MoveData(
    string Name,
    int StartupFrames,
    int ActiveFrames,
    int RecoveryFrames,
    int Damage,
    int HitstopFrames,
    Fp Pushback);
