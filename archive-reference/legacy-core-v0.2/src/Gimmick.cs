// Fighter.Core — character gimmicks as a CLOSED set (F2a).
// Core switches on GimmickId only. Per-character numbers live in FighterData.
// No arbitrary per-character exceptions in logic files (§46).
namespace Fighter.Core;

/// <summary>Character gimmicks as a closed set (Strategy selector, §46).</summary>
public enum GimmickId : byte
{
    /// <summary>No character gimmick (Glen: affinity Rage covers his identity).</summary>
    None = 0,
    /// <summary>Kira Acceleration (GDD §3.3): each connected special +1 stack (max 2 → ×3
    /// locomotion); ANY hp loss resets to 0. Data: FighterData.Gimmick.</summary>
    KiraAcceleration = 1,
}
