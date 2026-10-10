// Fighter.Core — simulation vs presentation events (§39).
// SimEvents change or record simulation truth. PresEvents are cosmetic triggers derived
// deterministically from sim events; Core never plays audio/particles itself.
namespace Fighter.Core;

/// <summary>Simulation event kinds (auditable combat log).</summary>
public enum SimEventType : byte
{
    /// <summary>Attack missed (invulnerability, out of range, knockdown invuln, air throw).</summary>
    Miss = 0,
    /// <summary>Clean hit (Value = final damage, Value2 = hitstop).</summary>
    Hit = 1,
    /// <summary>Blocked (Value = chip, Value2 = hitstop).</summary>
    Block = 2,
    /// <summary>Successful parry (no damage).</summary>
    Parry = 3,
    /// <summary>Same-rank clash, 12f rebound, no damage (Value2 = duration).</summary>
    Clash = 4,
    /// <summary>Throw connected (Value = damage).</summary>
    Throw = 5,
    /// <summary>Throw teched (no damage, separation).</summary>
    Tech = 6,
    /// <summary>Counter hit (Value = damage with ×1.2).</summary>
    CounterHit = 7,
    /// <summary>Knockout (A = winner).</summary>
    KO = 8,
    /// <summary>Rage activated.</summary>
    RageOn = 9,
    /// <summary>Rage expired (cooldown starts).</summary>
    RageOff = 10,
    /// <summary>Resonance manually activated.</summary>
    ResOn = 11,
    /// <summary>Resonance expired.</summary>
    ResOff = 12,
    /// <summary>Resonance auto-triggered (lifeline).</summary>
    ResAuto = 13,
    /// <summary>Counter stance reflected (Value = reflected damage).</summary>
    Reflected = 14,
    /// <summary>Round ended (A = winner, 2 = draw).</summary>
    RoundEnd = 15,
}

/// <summary>One simulation event: immutable audit record.</summary>
public readonly struct SimEvent
{
    /// <summary>Simulation frame.</summary>
    public readonly int Frame;
    /// <summary>Event kind.</summary>
    public readonly SimEventType Type;
    /// <summary>Actor fighter index (0/1).</summary>
    public readonly int A;
    /// <summary>Other fighter index.</summary>
    public readonly int B;
    /// <summary>Damage dealt (Hit/Block-chip/Throw/Reflected).</summary>
    public readonly int Value;
    /// <summary>Hitstop applied (or duration for Clash).</summary>
    public readonly int Value2;
    /// <summary>Was a counter-timing hit.</summary>
    public readonly bool Counter;

    /// <summary>Constructs an audit record (defaults: no damage, no hitstop, not counter).</summary>
    public SimEvent(int frame, SimEventType type, int a, int b, int value = 0, int value2 = 0, bool counter = false)
    { Frame = frame; Type = type; A = a; B = b; Value = value; Value2 = value2; Counter = counter; }
}

/// <summary>Presentation trigger kinds (cosmetic only; derived deterministically).</summary>
public enum PresKind : byte
{
    /// <summary>Hit spark VFX.</summary>
    HitSpark = 0,
    /// <summary>Block spark VFX.</summary>
    BlockSpark = 1,
    /// <summary>Parry flash (no hitstop).</summary>
    ParryFlash = 2,
    /// <summary>Clash spark VFX.</summary>
    ClashSpark = 3,
    /// <summary>Throw spark VFX.</summary>
    ThrowSpark = 4,
    /// <summary>KO slow-motion cue (presentation-only).</summary>
    KoSlowmo = 5,
    /// <summary>Rage aura VFX.</summary>
    RageAura = 6,
    /// <summary>Resonance shimmer VFX.</summary>
    ResShimmer = 7,
    /// <summary>Super startup flash.</summary>
    SuperFlash = 8,
}

/// <summary>One presentation trigger: frame, kind, position hint, attack level.</summary>
public readonly struct PresEvent
{
    /// <summary>Simulation frame.</summary>
    public readonly int Frame;
    /// <summary>Trigger kind.</summary>
    public readonly PresKind Kind;
    /// <summary>X hint, millipixels.</summary>
    public readonly int X;
    /// <summary>Y hint, millipixels.</summary>
    public readonly int Y;
    /// <summary>Attack level (VFX intensity).</summary>
    public readonly AttackLevel Level;
    /// <summary>Constructs a trigger.</summary>
    public PresEvent(int frame, PresKind kind, int x, int y, AttackLevel level)
    { Frame = frame; Kind = kind; X = x; Y = y; Level = level; }
}
