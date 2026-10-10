// Fighter.Core — motion matcher (§17: QCF/DP lives off the 8-frame ring).
// Directions are facing-RELATIVE: forward = toward opponent.
// ALLOCATION CONTRACT: Match is hot-path (every Step with a button edge) and
// allocates NOTHING on the managed heap — static helpers only (no capturing
// local functions, hence no display-class closures), stack-only spans for the
// QCF-ending scan, cached static delegates for predicates. Verified by the
// D7 allocation budget test.
namespace Fighter.Core;

/// <summary>Detected motion (facing-relative). Mapping to moves is the resolver's job.</summary>
public enum Motion : byte
{
    /// <summary>No motion on this edge.</summary>
    None = 0,
    /// <summary>QCF + punch edge.</summary>
    QcfPunch = 1,
    /// <summary>DP + kick edge.</summary>
    DpKick = 2,
    /// <summary>DP + punch edge (reported; mapping is the resolver's job).</summary>
    DpPunch = 3,
    /// <summary>Simplified 360 + punch edge.</summary>
    SpinPunch = 4,
    /// <summary>Double QCF + punch edge.</summary>
    DoubleQcfPunch = 5,
}

/// <summary>Motion matcher over the input ring (facing-relative directions).</summary>
public static class MotionMatcher
{
    /// <summary>Single-motion window, frames [CAL].</summary>
    public const int MotionWindow = 12;
    /// <summary>Simplified-360 window, frames [CAL].</summary>
    public const int SpinWindow = 15;
    /// <summary>Double-motion window, frames [CAL].</summary>
    public const int DoubleWindow = 20;

    private static bool IsPunch(Buttons b) =>
        (b & (Buttons.LP | Buttons.MP | Buttons.HP)) != Buttons.None;
    private static bool IsKick(Buttons b) =>
        (b & (Buttons.LK | Buttons.MK | Buttons.HK)) != Buttons.None;

    /// <summary>Match context (copied by value; small enough that no heap is involved).</summary>
    private readonly struct MatchCtx
    {
        public readonly RingBuffer<InputState> Hist;
        public readonly int Count;
        public readonly int Facing;
        public readonly InputState Cur;
        public readonly int CurFrame;

        public MatchCtx(RingBuffer<InputState> hist, int facing, InputState cur)
        {
            Hist = hist;
            Count = hist.Count;
            Facing = facing;
            Cur = cur;
            CurFrame = cur.Frame;
        }
    }

    /// <summary>Relative direction of a stored input: (fwdSign, downFlag, upFlag).</summary>
    private static (int fx, int dy) Relative(InputState s, int facing)
    {
        int fx = s.DirX == 0 ? 0 : (s.DirX == facing ? 1 : -1);
        return (fx, s.DirY);
    }

    // Sequence view, oldest→newest, plus current (allocation-free indexed reads).
    // Index i in [0, Count) reads history oldest-first; index Count is the current input.
    private static (int fx, int dy) Seq(MatchCtx ctx, int i) =>
        i < ctx.Count ? Relative(ctx.Hist.PeekOldest(i), ctx.Facing) : Relative(ctx.Cur, ctx.Facing);

    private static int FrameAt(MatchCtx ctx, int i) =>
        i < ctx.Count ? ctx.Hist.PeekOldest(i).Frame : ctx.CurFrame;

    private static bool Within(MatchCtx ctx, int idxFromEnd, int window)
    {
        int idx = ctx.Count - idxFromEnd;
        if (idx < 0) return false;
        return ctx.CurFrame - FrameAt(ctx, idx) <= window;
    }

    // Find last index (from end) matching predicate within window.
    private static int FindLast(MatchCtx ctx, System.Func<(int fx, int dy), bool> pred, int window, int startFromEnd = 0)
    {
        for (int o = startFromEnd; o <= ctx.Count; o++)
        {
            int idx = ctx.Count - o;
            if (!Within(ctx, o, window)) return -1;
            if (pred(Seq(ctx, idx))) return o;
        }
        return -1;
    }

    private static bool IsDown((int fx, int dy) d) => d.dy == -1 && d.fx == 0;
    private static bool IsDownFwd((int fx, int dy) d) => d.dy == -1 && d.fx == 1;
    private static bool IsFwd((int fx, int dy) d) => d.dy == 0 && d.fx == 1;
    private static bool IsBack((int fx, int dy) d) => d.dy == 0 && d.fx == -1;
    private static bool IsUp((int fx, int dy) d) => d.dy == 1;

    // DP: forward ... down ... downfwd, ordered, within window.
    private static bool Dp(MatchCtx ctx)
    {
        int oDf = FindLast(ctx, IsDownFwd, MotionWindow);
        if (oDf < 0) return false;
        int oD = -1, oF = -1;
        for (int o = oDf + 1; o <= ctx.Count; o++)
        {
            if (!Within(ctx, o, MotionWindow)) break;
            var d = Seq(ctx, ctx.Count - o);
            if (oD < 0 && IsDown(d)) oD = o;
            else if (oD >= 0 && IsFwd(d)) { oF = o; break; }
        }
        return oD >= 0 && oF >= 0;
    }

    // Simplified 360: F ... D ... B ... U in order within SpinWindow.
    private static bool Spin(MatchCtx ctx)
    {
        int oU = FindLast(ctx, IsUp, SpinWindow);
        if (oU < 0) return false;
        int stage = 0; // expect B, then D, then F going backwards
        for (int o = oU + 1; o <= ctx.Count; o++)
        {
            if (!Within(ctx, o, SpinWindow)) break;
            var d = Seq(ctx, ctx.Count - o);
            if (stage == 0 && IsBack(d)) stage = 1;
            else if (stage == 1 && IsDown(d)) stage = 2;
            else if (stage == 2 && IsFwd(d)) return true;
        }
        return false;
    }

    // QCF endings: every forward position that has ordered downfwd,down before it.
    // Returns the DISTINCT down-press count (two endings sharing one down-press,
    // e.g. held forward, count as ONE motion, never as a double-QCF) and the
    // total ending count via endingCount. Stack-only: no List, no HashSet, no LINQ.
    private static int QcfDistinctDowns(MatchCtx ctx, int window, out int endingCount)
    {
        // Bounded by the window in practice (≤ window+1 distinct downs); the cap
        // only guards pathological giant rings against stack overuse. Saturation
        // preserves every decision threshold (callers test >= 2 and >= 1).
        Span<int> seenDowns = stackalloc int[System.Math.Min(ctx.Count + 1, 65)];
        int seen = 0;
        endingCount = 0;
        for (int o = 0; o <= ctx.Count; o++)
        {
            if (!Within(ctx, o, window)) break;
            if (!IsFwd(Seq(ctx, ctx.Count - o))) continue;
            int oDf = -1;
            for (int q = o + 1; q <= ctx.Count; q++)
            {
                if (!Within(ctx, q, window)) break;
                var d = Seq(ctx, ctx.Count - q);
                if (oDf < 0 && IsDownFwd(d)) oDf = q;
                else if (oDf >= 0 && IsDown(d))
                {
                    endingCount++;
                    bool known = false;
                    for (int k = 0; k < seen; k++)
                        if (seenDowns[k] == q) { known = true; break; }
                    if (!known && seen < seenDowns.Length) seenDowns[seen++] = q;
                    break;
                }
            }
        }
        return seen;
    }

    /// <summary>Matches a motion ending on <paramref name="cur"/> (must carry a fresh
    /// punch/kick edge). Pure function of ring history + facing; allocation-free.</summary>
    public static Motion Match(RingBuffer<InputState> hist, InputState prev, InputState cur, int facing)
    {
        Buttons edge = cur.Buttons & ~prev.Buttons;
        bool pEdge = IsPunch(edge);
        bool kEdge = IsKick(edge);
        if (!pEdge && !kEdge) return Motion.None;

        var ctx = new MatchCtx(hist, facing, cur);
        if (pEdge)
        {
            if (Spin(ctx)) return Motion.SpinPunch;
            int distinctDowns = QcfDistinctDowns(ctx, DoubleWindow, out int endingCount);
            if (distinctDowns >= 2) return Motion.DoubleQcfPunch;
            if (endingCount >= 1)
            {
                // Single QCF only counts if its window fits MotionWindow, not just DoubleWindow.
                QcfDistinctDowns(ctx, MotionWindow, out int tightCount);
                if (tightCount >= 1) return Motion.QcfPunch;
            }
            if (Dp(ctx)) return Motion.DpPunch;
        }
        if (kEdge && Dp(ctx)) return Motion.DpKick;
        return Motion.None;
    }
}
