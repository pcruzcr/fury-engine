// Fighter.Core — fighter runtime state (every field participates in snapshot v5).
namespace Fighter.Core;

/// <summary>Mutable per-fighter simulation state. Plain data: the pipeline
/// (<see cref="FightSimulation"/>) owns all transitions. Fully snapshot-covered.</summary>
public sealed class Fighter
{
    /// <summary>Feet position, millipixels.</summary>
    public Vector2I Pos;
    /// <summary>Velocity, millipixels/frame.</summary>
    public Vector2I Vel;
    /// <summary>Facing: +1 right, −1 left. Updated only while both fighters are actionable.</summary>
    public int Facing = 1;

    /// <summary>Current state.</summary>
    public FighterState State = FighterState.Neutral;
    /// <summary>Frames spent in the current state.</summary>
    public int StateFrame;
    /// <summary>Total duration for timed states (0 = indefinite).</summary>
    public int StateDuration;

    /// <summary>Current attack move id (0 = none).</summary>
    public int MoveId;
    /// <summary>0-based elapsed frames of the current attack.</summary>
    public int MoveFrame;
    /// <summary>Effective startup (rage haste applied at move start).</summary>
    public int StartupEff = 1;
    /// <summary>Effective active frames.</summary>
    public int ActiveEff = 1;
    /// <summary>Effective recovery (rage haste applied at move start).</summary>
    public int RecoveryEff = 1;
    /// <summary>Dash distance covered during startup (SP.1-style moves, else 0).</summary>
    public int DashDist;
    /// <summary>Current attack already connected once (single-hit F1).</summary>
    public bool DidHit;
    /// <summary>Parried attacks cannot cancel.</summary>
    public bool NoCancel;
    /// <summary>Danimal armor consumed for this attack activation.</summary>
    public bool ArmorUsed;
    /// <summary>Heavy hit: knock down when hitstun expires.</summary>
    public bool PendingKnockdown;

    /// <summary>Current HP.</summary>
    public int Hp;
    /// <summary>Max HP (Danimal rage temporarily lowers the effective cap).</summary>
    public int MaxHp;
    /// <summary>EX meter, 0..3000 (100% = 3000).</summary>
    public int Ex;
    /// <summary>Resonance, 0..1000.</summary>
    public int Res;

    /// <summary>Rage affinity (damage modifiers + rage profile).</summary>
    public RageAffinity Affinity;
    /// <summary>Rage currently active.</summary>
    public bool RageActive;
    /// <summary>Rage frames remaining.</summary>
    public int RageTimer;
    /// <summary>Post-rage cooldown, frames remaining.</summary>
    public int RageCd;
    /// <summary>Resonance currently active.</summary>
    public bool ResActive;
    /// <summary>Resonance frames remaining.</summary>
    public int ResTimer;
    /// <summary>Resonance cooldown, frames remaining (runs from activation).</summary>
    public int ResCd;
    /// <summary>Round's resonance use consumed (manual or auto-trigger).</summary>
    public bool ResUsedRound;

    /// <summary>Frozen frames remaining (both fighters freeze together).</summary>
    public int Hitstop;
    /// <summary>Airborne flag (states alone never imply height).</summary>
    public bool Airborne;
    /// <summary>Airborne hits taken this juggle (cap = <see cref="JuggleMax"/>).</summary>
    public int JuggleCount;

    /// <summary>Scaling index as attacker (hits landed this combo).</summary>
    public int ComboGiven;
    /// <summary>HP received this combo (snowball cap, OPEN-005).</summary>
    public int ComboTaken;
    /// <summary>Consecutive blocked attacks (EX penalty from the 3rd, GDD §5.1).</summary>
    public int ConsecBlocked;
    /// <summary>Juggle cap, copied from data at spawn (data, not magic).</summary>
    public int JuggleMax = 3;

    /// <summary>Parry stance frames remaining.</summary>
    public int ParryStanceT;
    /// <summary>Parry whiff-recovery frames remaining.</summary>
    public int ParryWhiffT;
    /// <summary>OPEN-004 post-expiry grace (first Dant attempt only).</summary>
    public int ParryGraceT;
    /// <summary>Grace armed on the qualifying attempt, consumed at stance expiry.</summary>
    public bool ParryGraceArmed;
    /// <summary>Dant-rage first-parry guarantee still available (refreshed per rage).</summary>
    public bool FirstParryAvailable;
    /// <summary>Grace already consumed this rage (single use).</summary>
    public bool ParryGraceUsed;
    /// <summary>Counter-stance window remaining (Glen SP.3).</summary>
    public int CounterStanceT;
    /// <summary>Invulnerability frames remaining (backdash, super startup).</summary>
    public int InvulnT;
    /// <summary>Gimmick stage: 0 none, 1 rage aura (manual Burst excluded from MVP).</summary>
    public int RageStage;
    /// <summary>Motion special eats this frame's up-input (single-use).</summary>
    public bool SuppressJump;
    /// <summary>Kira stacks 0..2 → locomotion ×(1+stacks). Any hp loss resets.</summary>
    public int AccelStacks;
    /// <summary>One air action per jump (F1 rule).</summary>
    public bool AirAttackUsed;
    /// <summary>Gimmick id, copied from data at spawn (closed set, §46).</summary>
    public GimmickId Gimmick;
    /// <summary>EX zone multiplier, per-mille (1000 neutral, 1050 Dojo zone).</summary>
    public int ExZonePerMille = 1000;
    /// <summary>Locomotion zone multiplier, per-mille (1000 neutral, 900 Dojo zone).</summary>
    public int SpeedZonePerMille = 1000;

    /// <summary>Last consumed input (debugging/training).</summary>
    public InputState LastInput;

    /// <summary>Per-fighter gimmick state blob (128 bytes, zero until a gimmick
    /// claims bytes; schema 5). Reserved today: no gimmick writes yet.</summary>
    public GimmickData GimmickState;

    /// <summary>Phase-4 intent for the current frame (see
    /// <see cref="CommandNormalResolver"/>; schema 5). Informational: the fight
    /// authority (<see cref="MoveResolver"/>) still decides what starts.</summary>
    public int LastCommandNormal;

    /// <summary>Enters <paramref name="state"/> for <paramref name="duration"/> frames (0 = indefinite).</summary>
    public void Enter(FighterState state, int duration = 0)
    {
        State = state;
        StateFrame = 0;
        StateDuration = duration;
    }
}
