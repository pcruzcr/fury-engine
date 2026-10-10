// Fighter.Core — deterministic CPU brain (SCENARIO-018, §44/§47).
// Pure function of (frame, sim state) → InputState through the SAME input path as a
// human (RingBuffer → resolver → sim). Priority is convergence proof (AI games replay),
// not intelligence: approach, block strings, poke, hop, parry attempt, low block,
// backdash pattern. Reaction granularity is frame-count modular arithmetic — fully
// deterministic, zero RNG, zero allocations.
namespace Fighter.Core;

/// <summary>Scripted sparring brain for fighter index 1 (P2 slot). Deterministic.</summary>
public static class CpuBrain
{
    /// <summary>Computes P2's input for <paramref name="frame"/> from live sim state.</summary>
    public static InputState ForP2(FightSimulation sim, int frame)
    {
        var me = sim.Fighters[1];
        var op = sim.Fighters[0];
        int gap = op.Pos.X > me.Pos.X ? op.Pos.X - me.Pos.X : me.Pos.X - op.Pos.X;
        int toward = me.Facing;
        int away = -me.Facing;
        InputState N(Buttons b, int dx, int dy) => new(b, dx, dy, frame);

        if (!FighterStateMeta.IsActionable(me.State)) return N(Buttons.None, 0, 0);
        if (op.State == FighterState.Attack && gap < 160000 && frame % 5 < 3)
            return N(Buttons.None, away, 0); // block strings
        if (gap > 140000) return N(Buttons.None, toward, 0); // approach
        if (op.State is FighterState.Hitstun or FighterState.Blockstun)
            return frame % 3 == 0 ? N(Buttons.LP, 0, 0) : N(Buttons.None, toward, 0); // pressure
        if (gap > 160000 && frame % 97 == 0) return N(Buttons.None, toward, 1); // hop in (approach only)
        if (frame % 251 == 0) return N(Buttons.LP | Buttons.MP, 0, 0); // rare parry attempt
        if (frame % 53 == 0) return N(Buttons.None, away, -1); // low block sometimes
        // Poke rarely: recovery (~22f) must end well before the next poke, otherwise the
        // CPU is perpetually busy and never blocks (proven by test, 2026-09-13).
        if (frame % 67 == 0) return N((frame / 67) % 2 == 0 ? Buttons.MP : Buttons.HP, 0, 0);
        if (frame % 40 <= 2 && gap < 200000 && gap > 60000)
        {
            int t = frame % 40;
            if (t == 0) return N(Buttons.None, away, 0);
            if (t == 1) return N(Buttons.None, 0, 0);
            return N(Buttons.None, away, 0); // double-tap back → backdash
        }
        return N(Buttons.None, toward, 0);
    }
}
