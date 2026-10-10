// Fighter.Core — F1 calibration constants (ADR-002).
// Every value the GDD does not specify lives HERE, marked [CAL] = pending the feel pass.
// Nothing in logic files may hardcode a combat number. All integers, all frames.
namespace Fighter.Core;

/// <summary>Centralized F1 calibration. [CAL] = chosen for F1, must be validated by feel.</summary>
public static class Calibration
{
    // ---- Locomotion [CAL] ----
    /// <summary>Walk speed, millipixels/frame [CAL].</summary>
    public const int WalkSpeed = 2500;
    /// <summary>Jump horizontal speed [CAL].</summary>
    public const int JumpVelX = 3500;
    /// <summary>Jump vertical speed, negative = up [CAL].</summary>
    public const int JumpVelY = -14000;
    /// <summary>Gravity, millipixels/frame² [CAL].</summary>
    public const int Gravity = 800;
    /// <summary>Body half-width, millipixels (30px) [CAL].</summary>
    public const int FighterHalfWidth = 30000;
    /// <summary>Body height, millipixels (120px) [CAL].</summary>
    public const int FighterHeight = 120000;
    /// <summary>Stage left bound.</summary>
    public const int StageMinX = 0;
    /// <summary>Stage right bound (960px).</summary>
    public const int StageMaxX = 960000;
    /// <summary>Ground line Y (up-negative).</summary>
    public const int GroundY = 0;

    // ---- Backdash (GDD: invuln 1-6f; duration/double-tap [CAL]) ----
    /// <summary>Backdash total duration, frames [CAL].</summary>
    public const int BackdashDuration = 16;
    /// <summary>Backdash speed during invulnerable part [CAL].</summary>
    public const int BackdashSpeed = 6000;
    /// <summary>Double-tap detection window, frames [CAL].</summary>
    public const int DoubleTapWindow = 10;

    // ---- Hitstun: GDD §4.4 range points ----
    // Medium = 14 (TOP of the 10-14 range): required so chained follow-ups with
    // 13f startup (HK) link as TRUE combos (BnB/launcher routes, DF-CON-003).
    // Light = 8 (TOP of the 6-8 range): required so chained 7f-startup MP links off LP.
    /// <summary>Light hitstun, frames (GDD range 6-8 top: route links).</summary>
    public const int HitstunLight = 8;
    /// <summary>Medium hitstun, frames (GDD range 10-14 top: route links).</summary>
    public const int HitstunMedium = 14;
    /// <summary>Heavy hitstun, frames (GDD range 16-22 midpoint), then knockdown.</summary>
    public const int HitstunHeavy = 19;
    /// <summary>Knockdown duration, frames [CAL].</summary>
    public const int KnockdownDuration = 30;
    /// <summary>Wakeup duration, frames (GDD §4.4).</summary>
    public const int WakeupDuration = 8;

    // ---- Parry (GDD: 6f window, 10f Dant; whiff-recovery + grace [CAL]) ----
    /// <summary>Parry stance window, frames (GDD §4.6).</summary>
    public const int ParryWindow = 6;
    /// <summary>Parry stance in Dant Rage, frames (GDD §5.2).</summary>
    public const int ParryWindowDantRage = 10;
    /// <summary>Whiffed-parry recovery, frames [CAL] (hit during it deals ×1.2).</summary>
    public const int ParryWhiffRecovery = 12;
    /// <summary>First-Dant-attempt post-expiry grace, frames (OPEN-004).</summary>
    public const int ParryGraceFirstDant = 2;
    /// <summary>Attacker stun on successful parry, frames (GDD §4.4).</summary>
    public const int ParryStunAttacker = 15;

    // ---- Clash (GDD: rebote neutro 12f, sin daño) ----
    /// <summary>Clash rebound, frames, no damage (GDD §4.5).</summary>
    public const int ClashStun = 12;
    /// <summary>Super-vs-super mutual stun, frames, no damage (GDD §4.5).</summary>
    public const int SuperVsSuperStun = 20;

    // ---- Pushback, millipixels [CAL] ----
    /// <summary>Attacker pushback, light [CAL].</summary>
    public const int PushAttackerL = 500;
    /// <summary>Attacker pushback, medium/special [CAL].</summary>
    public const int PushAttackerM = 1000;
    /// <summary>Attacker pushback, heavy/super [CAL].</summary>
    public const int PushAttackerH = 2000;
    /// <summary>Defender pushback, light [CAL].</summary>
    public const int PushDefenderL = 2000;
    /// <summary>Defender pushback, medium/special [CAL].</summary>
    public const int PushDefenderM = 3500;
    /// <summary>Defender pushback, heavy/super [CAL].</summary>
    public const int PushDefenderH = 5000;

    // ---- Throw (GDD: tech LP+LK 10f; range [CAL]) ----
    /// <summary>Throw range, edge-to-edge millipixels [CAL].</summary>
    public const int ThrowRange = 80000;
    /// <summary>Throw-tech window, frames (GDD §4.6).</summary>
    public const int TechWindow = 10;
    /// <summary>Post-tech separation, millipixels [CAL].</summary>
    public const int TechSeparation = 40000;

    // ---- Launcher / juggle physics [CAL] ----
    // LauncherVelY sized so GDD launcher routes are executable (DF-CON-003): ~35f victim airtime.
    // LauncherVelX = 0 (near-vertical pop): horizontal knockback comes from the instant Push();
    // sustained drift would carry the victim out of juggle reach (proven by test, 2026-09-13).
    /// <summary>Launcher pop, vertical millipixels/frame [CAL] (~35f victim airtime).</summary>
    public const int LauncherVelY = -14000;
    /// <summary>Launcher pop, horizontal (0 = near-vertical; drift breaks juggles) [CAL].</summary>
    public const int LauncherVelX = 0;
    /// <summary>Downward slam when the juggle cap is exceeded [CAL].</summary>
    public const int JuggleSlamVelY = 6000;

    // ---- Active/recovery for moves where GDD gives startup/onBlock/damage only [CAL] ----
    /// <summary>Active frames, light [CAL].</summary>
    public const int ActiveLight = 2;
    /// <summary>Active frames, medium [CAL].</summary>
    public const int ActiveMedium = 3;
    /// <summary>Active frames, heavy [CAL].</summary>
    public const int ActiveHeavy = 3;
    /// <summary>Active frames, special [CAL].</summary>
    public const int ActiveSpecial = 4;
    /// <summary>Active frames, super [CAL].</summary>
    public const int ActiveSuper = 6;
    /// <summary>Recovery frames, light [CAL].</summary>
    public const int RecoveryLight = 8;
    /// <summary>Recovery frames, medium [CAL].</summary>
    public const int RecoveryMedium = 12;
    /// <summary>Recovery frames, heavy [CAL].</summary>
    public const int RecoveryHeavy = 18;
    /// <summary>Recovery frames, special [CAL].</summary>
    public const int RecoverySpecial = 16;
    /// <summary>Recovery frames, super [CAL].</summary>
    public const int RecoverySuper = 30;
}
