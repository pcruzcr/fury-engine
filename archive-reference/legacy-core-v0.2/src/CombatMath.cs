// Fighter.Core — named integer combat math (R1 refactor).
// WHY: every multiplier below is a SPECIFIED rule (GDD or resolved OPEN), never a magic
// number. Centralizing them makes each call site self-documenting, auditable in one place,
// and unit-testable independently of combat flow. All operations are integer-only with
// documented rounding (truncation toward zero via / operator).
namespace Fighter.Core;

/// <summary>Specified combat multipliers as named integer operations.</summary>
public static class CombatMath
{
    /// <summary>Counter hit: +20% (GDD §4.6). Rounds down.</summary>
    public static int CounterHit(int v) => v * 6 / 5;

    /// <summary>Parry-whiff punish: +20% incoming (GDD §4.6 fail case). Rounds down.</summary>
    public static int ParryFail(int v) => v * 6 / 5;

    /// <summary>Resonance damage taken: −20% (OPEN-003). Rounds down.</summary>
    public static int ResonanceTaken(int v) => v * 4 / 5;

    /// <summary>Resonance pushback: +20% (OPEN-003). Rounds down.</summary>
    public static int ResonancePushback(int v) => v * 6 / 5;

    /// <summary>Danimal physical: +25% (GDD §5.2). Rounds down.</summary>
    public static int DanimalPhysical(int v) => v * 5 / 4;

    /// <summary>Dant energy: +25% (GDD §5.2). Rounds down.</summary>
    public static int DantEnergy(int v) => v * 5 / 4;

    /// <summary>Gray all: +10% (GDD §5.2). Rounds down.</summary>
    public static int GrayAll(int v) => v * 11 / 10;

    /// <summary>Danimal attack speed: +20% faster → frame counts ×5/6, min 1 (F1 rule).</summary>
    public static int DanimalHaste(int frames) => System.Math.Max(1, frames * 5 / 6);

    /// <summary>Dant stability: −5% locomotion (GDD §5.2). Rounds down.</summary>
    public static int DantSlow(int speed) => speed * 19 / 20;

    /// <summary>Danimal aggression: +15% locomotion (GDD §5.2). Rounds down.</summary>
    public static int DanimalSwift(int speed) => speed * 23 / 20;

    /// <summary>Counter-stance reflect: ×1.5 of incoming base (GDD §3.2 SP.3). Rounds down.</summary>
    public static int Reflect(int baseDamage) => baseDamage * 3 / 2;

    /// <summary>Chip: 10% of final damage (OPEN-001). Rounds down; may be 0 for tiny hits.</summary>
    public static int Chip(int finalDamage) => finalDamage / 10;

    /// <summary>Danimal max-life cost: −10% while raging (GDD §5.2). Rounds down.</summary>
    public static int DanimalMaxHp(int maxHp) => maxHp * 9 / 10;

    /// <summary>Auto-resonance lifeline: triggers below 15% max HP (OPEN-003). Pure predicate.</summary>
    public static bool IsLifeline(int hpAfterHit, int maxHp) => hpAfterHit < maxHp * 15 / 100;

    /// <summary>Snowball cap: damage received beyond 40% max HP in one combo (OPEN-005).</summary>
    public static int SnowballCap(int maxHp) => maxHp * 40 / 100;
}
