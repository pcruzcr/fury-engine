// Fighter.Core — move data model + validator (ADR-002 §1, §51/§52).
namespace Fighter.Core;

/// <summary>Damage/priority tier. Rank order: Light &lt; Medium &lt; Heavy &lt; Special &lt; Super.</summary>
public enum AttackLevel : byte
{
    /// <summary>No tier (throws).</summary>
    None = 0,
    /// <summary>Light: 4f hitstop, loses to Medium+.</summary>
    Light = 1,
    /// <summary>Medium: 6f hitstop.</summary>
    Medium = 2,
    /// <summary>Heavy: 10f hitstop, knockdown-class.</summary>
    Heavy = 3,
    /// <summary>Special: beats all normals.</summary>
    Special = 4,
    /// <summary>Super: beats everything except Super.</summary>
    Super = 5,
}

/// <summary>Move category (resolution path + cancel legality).</summary>
public enum MoveKind : byte
{
    /// <summary>Base normal (neutral input).</summary>
    Normal,
    /// <summary>Directional normal (evaluated pre-FSM, Fase 4).</summary>
    CommandNormal,
    /// <summary>Motion-gated special.</summary>
    Special,
    /// <summary>Meter-burned super (EX-gated).</summary>
    Super,
    /// <summary>Unblockable grab (range-checked, techable unless command grab).</summary>
    Throw,
}

/// <summary>Directional requirement, resolved relative to facing (forward = toward opponent).</summary>
public enum DirReq : byte
{
    /// <summary>No direction.</summary>
    Neutral,
    /// <summary>Toward opponent.</summary>
    Forward,
    /// <summary>Away from opponent.</summary>
    Back,
    /// <summary>Down-away (sweep).</summary>
    DownBack,
}

/// <summary>Combat properties bitmask (blocking, juggle, rage interaction, stances).</summary>
[Flags]
public enum MoveFlags : uint
{
    /// <summary>No special properties (mid).</summary>
    None = 0,
    /// <summary>Must be blocked low.</summary>
    Low = 1 << 0,
    /// <summary>Must be blocked high.</summary>
    Overhead = 1 << 1,
    /// <summary>Lifts victim airborne (juggle starter).</summary>
    Launcher = 1 << 2,
    /// <summary>Forces knockdown on hit.</summary>
    Knockdown = 1 << 3,
    /// <summary>Boosted by Danimal rage (vs energy/Dant).</summary>
    Physical = 1 << 4,
    /// <summary>Boosted by Dant rage.</summary>
    Energy = 1 << 5,
    /// <summary>Stance: first hit taken is reflected ×1.5 (Glen SP.3, 30f).</summary>
    Counter = 1 << 6,
}

/// <summary>One attack definition. Frames are absolute counts; damage is base HP.</summary>
public sealed class MoveData
{
    /// <summary>Stable numeric id (per-fighter namespace).</summary>
    public int Id;
    /// <summary>Display name (matches GDD).</summary>
    public string Name = "";
    /// <summary>Category (resolution path).</summary>
    public MoveKind Kind;
    /// <summary>Priority/damage tier.</summary>
    public AttackLevel Level;
    /// <summary>Required button(s); None for motion-gated moves.</summary>
    public Buttons Buttons;
    /// <summary>Directional requirement (facing-relative).</summary>
    public DirReq Dir;
    /// <summary>Startup frames (GDD value where specified).</summary>
    public int Startup;
    /// <summary>Active frames ([CAL] where GDD silent).</summary>
    public int Active;
    /// <summary>Recovery frames ([CAL] where GDD silent).</summary>
    public int Recovery;
    /// <summary>Signed block advantage (GDD value; drives the blockstun formula).</summary>
    public int OnBlock;
    /// <summary>Base damage HP (GDD value, pre-scale).</summary>
    public int Damage;
    /// <summary>Combat properties.</summary>
    public MoveFlags Flags;
    /// <summary>Meter cost: 0 normals/regular specials, 1000 EX special, 2000 super.</summary>
    public int ExCost;
    /// <summary>True only for meter-burned EX versions (MVP-excluded path).</summary>
    public bool IsEx;
    /// <summary>Motion-gated (QCF/DP/360/double): resolved from the ring buffer, not buttons.
    /// Motion specials eat jump inputs (input priority). Only Special/Super/Throw kinds.</summary>
    public bool MotionGated;
    /// <summary>Usable while airborne (Jump state). F1: Normal kind, neutral-dir only.</summary>
    public bool AirOK;
    /// <summary>Target-combo chains transcribed from GDD routes (BnB LP&gt;MP, corner F+HP&gt;B+HP...).
    /// Normal→normal cancels are legal ONLY through this list (data, not Core exceptions).</summary>
    public int[] ChainTo = System.Array.Empty<int>();
}

/// <summary>Computed per-attack hitstop. GDD §4.3 table. Counter hits add +2 at resolution.</summary>
public static class HitstopTable
{
    /// <summary>Hitstop for a move: Super 20, EX 12, else by level (Special non-EX uses Medium 6).</summary>
    public static int For(MoveData move)
    {
        if (move.Kind == MoveKind.Super) return 20;
        if (move.IsEx) return 12; // EX Special (MVP-excluded; path reserved + tested)
        return move.Level switch
        {
            AttackLevel.Light => 4,
            AttackLevel.Medium => 6,
            AttackLevel.Heavy => 10,
            AttackLevel.Special => 6, // regular special: priority Special, impact Medium
            _ => 0,
        };
    }
}

/// <summary>Loud data validation: invalid content must never fail silently (§52).</summary>
public static class DataValidator
{
    /// <summary>Validates a whole fighter: top-level stats, motion map integrity, then moves.</summary>
    public static void ValidateFighter(FighterData d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) throw new InvalidOperationException("Fighter needs a name.");
        if (d.MaxHp <= 0) throw new InvalidOperationException($"{d.Name}: MaxHp must be > 0.");
        if (d.WalkSpeed <= 0) throw new InvalidOperationException($"{d.Name}: WalkSpeed must be > 0.");
        if (d.JuggleMax is < 1 or > 5)
            throw new InvalidOperationException($"{d.Name}: JuggleMax {d.JuggleMax} outside 1..5.");
        if (d.Moves.Count == 0) throw new InvalidOperationException($"{d.Name}: no moves.");
        ValidateMoves(d.Moves);
        var ids = new System.Collections.Generic.HashSet<int>();
        foreach (var m in d.Moves) ids.Add(m.Id);
        foreach (int refId in new[] { d.QcfShort, d.QcfMid, d.QcfFar, d.DpKickMove, d.SpinMove, d.DoubleQcfMove })
            if (refId != 0 && !ids.Contains(refId))
                throw new InvalidOperationException($"{d.Name}: motion map references unknown move {refId}.");
    }

    /// <summary>Validates a move list: ids, frames, buttons, flags, costs, chains.</summary>
    public static void ValidateMoves(System.Collections.Generic.IReadOnlyList<MoveData> moves)
    {
        var ids = new System.Collections.Generic.HashSet<int>();
        foreach (var m in moves)
        {
            if (!ids.Add(m.Id)) throw new InvalidOperationException($"Duplicate move id {m.Id} ({m.Name}).");
            if (string.IsNullOrWhiteSpace(m.Name)) throw new InvalidOperationException($"Move {m.Id} has no name.");
            if (m.Startup <= 0) throw new InvalidOperationException($"{m.Name}: startup must be > 0.");
            if (m.Active <= 0) throw new InvalidOperationException($"{m.Name}: active must be > 0.");
            if (m.Recovery <= 0) throw new InvalidOperationException($"{m.Name}: recovery must be > 0.");
            if (m.Damage < 0) throw new InvalidOperationException($"{m.Name}: damage must be >= 0.");
            if (m.Kind is MoveKind.Normal or MoveKind.CommandNormal
                && m.Buttons == Buttons.None)
                throw new InvalidOperationException($"{m.Name}: button moves need buttons.");
            // Specials / Supers / command-grabs may carry Buttons.None: they are motion-gated.
            if (m.Kind == MoveKind.Throw && m.Level != AttackLevel.None)
                throw new InvalidOperationException($"{m.Name}: throws carry no attack level.");
            if ((m.Flags & (MoveFlags.Low | MoveFlags.Overhead)) == (MoveFlags.Low | MoveFlags.Overhead))
                throw new InvalidOperationException($"{m.Name}: cannot be both Low and Overhead.");
            if (m.ExCost < 0 || m.ExCost > SimulationUnits.ExMax)
                throw new InvalidOperationException($"{m.Name}: EX cost out of range.");
            if (m.MotionGated && m.Kind is MoveKind.Normal or MoveKind.CommandNormal)
                throw new InvalidOperationException($"{m.Name}: normals are button-resolved, never motion-gated.");
            if (m.AirOK && m.Kind != MoveKind.Normal)
                throw new InvalidOperationException($"{m.Name}: F1 air attacks are normals only.");
            if (m.AirOK && m.Dir != DirReq.Neutral)
                throw new InvalidOperationException($"{m.Name}: F1 air attacks are neutral-dir only.");
            if (m.ChainTo.Length > 8)
                throw new InvalidOperationException($"{m.Name}: chain list implausibly long.");
        }
        var byId = new System.Collections.Generic.HashSet<int>(ids);
        foreach (var m in moves)
            foreach (int t in m.ChainTo)
            {
                if (t == m.Id) continue; // self-chain (LP>LP) is legal
                if (!byId.Contains(t))
                    throw new InvalidOperationException($"{m.Name}: chains to unknown move {t}.");
            }
    }
}
