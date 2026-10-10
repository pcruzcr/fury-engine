// Fighter.Core — move resolution (fight authority since F1; the F0 button-only seam
// was removed in refactor R1 as TECH-DEBT-001 — this data-driven resolver supersedes it).
// Data in, move id out. No character exceptions: FighterData carries motions, chains, buttons.
namespace Fighter.Core;

/// <summary>Resolution context for one fighter on one frame (all inputs, no side effects).</summary>
public readonly struct ResolveContext
{
    /// <summary>Character data (moves, chains, motion map).</summary>
    public readonly FighterData Data;
    /// <summary>Facing (+1/−1): forward is relative to this.</summary>
    public readonly int Facing;
    /// <summary>Previous frame input (edge detection).</summary>
    public readonly InputState Prev;
    /// <summary>Current frame input.</summary>
    public readonly InputState Cur;
    /// <summary>Input history ring (already includes <see cref="Cur"/>).</summary>
    public readonly RingBuffer<InputState> History;
    /// <summary>Fighter state.</summary>
    public readonly FighterState State;
    /// <summary>Current attack already connected (cancel rules).</summary>
    public readonly bool DidHit;
    /// <summary>Kind of the in-progress attack (cancel legality).</summary>
    public readonly MoveKind ActiveKind;
    /// <summary>Id of the in-progress attack (chain lookup).</summary>
    public readonly int ActiveMoveId;
    /// <summary>One fresh air action per jump; chains still allowed.</summary>
    public readonly bool AirAttackUsed;
    /// <summary>Airborne flag (Attack mid-air is still air).</summary>
    public readonly bool Airborne;
    /// <summary>Current meter (super gating).</summary>
    public readonly int Ex;
    /// <summary>Attacker is behind the victim (back-throw selection).</summary>
    public readonly bool BehindVictim; // positional (for Estrangula)

    /// <summary>Builds the context (all inputs required; no defaults, no hidden state).</summary>
    public ResolveContext(FighterData data, int facing, InputState prev, InputState cur,
        RingBuffer<InputState> history, FighterState state, bool didHit, MoveKind activeKind,
        int activeMoveId, bool airAttackUsed, bool airborne, int ex, bool behindVictim)
    {
        Data = data; Facing = facing; Prev = prev; Cur = cur; History = history;
        State = state; DidHit = didHit; ActiveKind = activeKind; ActiveMoveId = activeMoveId;
        AirAttackUsed = airAttackUsed; Airborne = airborne; Ex = ex; BehindVictim = behindVictim;
    }
}

/// <summary>Data-driven move resolver (fight authority). FighterData in, move id out:
/// 0 = no action, −1 = motion consumed by a gated attempt (no fallback).</summary>
public static class MoveResolver
{
    /// <summary>Resolves one fighter's fresh action: motions → throws → command normals → normals.
    /// Honors cancel windows (hit-confirmed) and air rules. Pure function, no side effects.
    /// Returns move id; 0 = no action, −1 = motion consumed by a gated attempt (no fallback).</summary>
    public static int Resolve(in ResolveContext ctx)
    {
        bool actionable = FighterStateMeta.IsActionable(ctx.State);
        bool cancelWindow = ctx.State == FighterState.Attack && ctx.DidHit &&
            (ctx.ActiveKind is MoveKind.Normal or MoveKind.CommandNormal or MoveKind.Special);
        bool airborne = ctx.Airborne; // BUGFIX F2a: State==Attack mid-air is still air.
        if (!actionable && !cancelWindow && !airborne) return 0;

        Buttons edge = ctx.Cur.Buttons & ~ctx.Prev.Buttons;

        // Air: AirOK normals by buttons only (F1: neutral-dir, no motions/throws/parry).
        // Fresh air starts need a clean jump (!AirAttackUsed); chains off a connected
        // air attack are still legal (juggle routes).
        if (airborne && !cancelWindow)
        {
            if (edge == Buttons.None || ctx.AirAttackUsed) return 0;
            foreach (var m in ctx.Data.Moves)
            {
                if (!m.AirOK) continue;
                if ((edge & m.Buttons) == Buttons.None) continue;
                return m.Id;
            }
            return 0;
        }
        if (airborne && cancelWindow)
        {
            if (edge == Buttons.None) return 0;
            foreach (var m in ctx.Data.Moves)
            {
                if (!m.AirOK) continue;
                if ((edge & m.Buttons) == Buttons.None) continue;
                if (!CancelLegal(ctx.ActiveKind, ctx.ActiveMoveId, m, ctx.Data)) continue;
                return m.Id;
            }
            return 0;
        }
        if (edge == Buttons.None && cancelWindow) return 0; // cancels still need fresh input

        // 1. Motions (highest authority among fresh inputs).
        Motion motion = MotionMatcher.Match(ctx.History, ctx.Prev, ctx.Cur, ctx.Facing);
        int special = ResolveMotion(ctx, motion, edge);
        if (special != 0)
        {
            if (!actionable && !CancelLegal(ctx.ActiveKind, ctx.ActiveMoveId, ctx.Data.Get(special), ctx.Data)) return 0;
            return special;
        }

        // 2. Throw (HP+HK edge, TECHABLE normal throws; command grab handled via motion).
        if ((edge & (Buttons.HP | Buttons.HK)) == (Buttons.HP | Buttons.HK) && actionable)
            return ctx.BehindVictim ? GlenData.ThrowBack : GlenData.ThrowFwd;

        // 3. Buttons + relative direction → command normal first, then neutral normal.
        // In a cancel window the candidate must additionally satisfy cancel legality.
        if (edge == Buttons.None) return 0;
        int rel = RelativeDir(ctx.Cur, ctx.Facing);
        // rel: +1 forward, -1 back, -2 down-back, 0 neutral/other
        int candidate = ScanButtons(ctx, edge, rel, MoveKind.CommandNormal);
        if (candidate == 0) candidate = ScanButtons(ctx, edge, rel, MoveKind.Normal);
        if (candidate == 0) return 0;
        if (!actionable && !CancelLegal(ctx.ActiveKind, ctx.ActiveMoveId, ctx.Data.Get(candidate), ctx.Data)) return 0;
        return candidate;
    }

    /// <summary>Button scan for one move kind (command normals check direction too).</summary>
    private static int ScanButtons(in ResolveContext ctx, Buttons edge, int rel, MoveKind kind)
    {
        foreach (var m in ctx.Data.Moves)
        {
            if (m.Kind != kind) continue;
            if (kind == MoveKind.Normal && m.Dir != DirReq.Neutral) continue;
            if ((edge & m.Buttons) == Buttons.None) continue;
            if (kind == MoveKind.CommandNormal && !DirMatches(m.Dir, rel, edge, m.Buttons)) continue;
            return m.Id;
        }
        return 0;
    }

    private static int RelativeDir(InputState cur, int facing)
    {
        int fx = cur.DirX == 0 ? 0 : (cur.DirX == facing ? 1 : -1);
        if (fx == -1 && cur.DirY == -1) return -2;
        return fx;
    }

    private static bool DirMatches(DirReq req, int rel, Buttons edge, Buttons moveButtons)
    {
        // Edge must include the move's exact button(s) for command normals (no button spill).
        if ((edge & moveButtons) != moveButtons) return false;
        return req switch
        {
            DirReq.Forward => rel == 1,
            DirReq.Back => rel == -1,
            DirReq.DownBack => rel == -2,
            _ => rel == 0,
        };
    }

    private static bool CancelLegal(MoveKind from, int fromId, MoveData to, FighterData data)
    {
        if (to.Kind == MoveKind.Super) return from is MoveKind.Normal or MoveKind.CommandNormal or MoveKind.Special;
        if (to.Kind == MoveKind.Special) return from is MoveKind.Normal or MoveKind.CommandNormal;
        if (to.Kind is MoveKind.Normal or MoveKind.CommandNormal)
        {
            // Target chains: legal only through the source move's transcribed ChainTo list.
            if (from is not (MoveKind.Normal or MoveKind.CommandNormal)) return false;
            foreach (var m in data.Moves)
                if (m.Id == fromId)
                    return System.Array.IndexOf(m.ChainTo, to.Id) >= 0;
            return false;
        }
        return false; // throws never cancel anything
    }

    private static int ResolveMotion(in ResolveContext ctx, Motion motion, Buttons edge)
    {
        var map = ctx.Data;
        switch (motion)
        {
            case Motion.DoubleQcfPunch:
                if (map.DoubleQcfMove == 0) return 0; // unsupported: fall through to buttons
                var super = ctx.Data.Get(map.DoubleQcfMove);
                if (ctx.Ex >= super.ExCost) return map.DoubleQcfMove;
                return -1; // gated: consume the edge, no silent fallback
            case Motion.QcfPunch:
                int variant = (edge & Buttons.HP) != Buttons.None ? map.QcfFar
                    : (edge & Buttons.MP) != Buttons.None ? map.QcfMid : map.QcfShort;
                return variant; // 0 when unsupported → fall through to buttons
            case Motion.DpKick:
                return map.DpKickMove;
            case Motion.SpinPunch:
                return map.SpinMove; // command grab: untechable (documented, GDD §3.2)
            default:
                return 0;
        }
    }
}
