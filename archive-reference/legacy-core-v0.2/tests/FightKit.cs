// F1 test kit: frame-aware stepping, range closing, Glen mirror matches.
using Fighter.Core;

namespace Fighter.Core.Tests;

internal static class FightKit
{
    internal static readonly FighterData Glen = GlenData.Build();
    internal static readonly FighterData Kira = KiraData.Build();

    internal static FightSimulation New(ulong seed = 1, int roundTimer = 80 * 60) =>
        new(seed, Glen, Glen, roundTimer);

    internal static FightSimulation NewVsKira(ulong seed = 1) =>
        new(seed, Glen, Kira);

    internal static InputState I(Buttons b, int dx, int dy, int frame) => new(b, dx, dy, frame);

    internal static InputState Neutral(int frame) => new(Buttons.None, 0, 0, frame);

    /// <summary>Steps once with explicit inputs, frame-stamped from the sim (motion windows need real frames).</summary>
    internal static void T(FightSimulation s,
        Buttons b0, int dx0, int dy0, Buttons b1, int dx1, int dy1)
    {
        s.Step(I(b0, dx0, dy0, s.Frame), I(b1, dx1, dy1, s.Frame));
    }

    internal static void Neutrals(FightSimulation s, int n)
    {
        for (int k = 0; k < n; k++) T(s, Buttons.None, 0, 0, Buttons.None, 0, 0);
    }

    internal static int Gap(FightSimulation s) =>
        System.Math.Abs(s.Fighters[0].Pos.X - s.Fighters[1].Pos.X);

    /// <summary>Both walk toward each other until center gap &lt;= maxGap (or 400 steps).</summary>
    internal static void WalkIntoRange(FightSimulation s, int maxGap = 90000)
    {
        for (int k = 0; k < 400 && Gap(s) > maxGap && !s.Over; k++)
            T(s, Buttons.None, s.Fighters[0].Facing, 0, Buttons.None, s.Fighters[1].Facing, 0);
    }

    /// <summary>Single button tap for fighter idx (neutral before/after). Other fighter neutral.</summary>
    internal static void Tap(FightSimulation s, int idx, Buttons b, int dx = 0, int dy = 0)
    {
        if (idx == 0) T(s, b, dx, dy, Buttons.None, 0, 0);
        else T(s, Buttons.None, 0, 0, b, dx, dy);
    }

    /// <summary>Tap for one fighter while the other KEEPS an exact held input (no release edge).
    /// Mandatory whenever the other fighter must not produce edges (block/tech holds).</summary>
    internal static void TapKeep(FightSimulation s, int idx,
        Buttons b, int dx, int dy, Buttons ob, int odx, int ody)
    {
        if (idx == 0) T(s, b, dx, dy, ob, odx, ody);
        else T(s, ob, odx, ody, b, dx, dy);
    }

    internal static bool HasSimEvent(FightSimulation s, SimEventType type)
    {
        foreach (var e in s.LastSimEvents)
            if (e.Type == type) return true;
        return false;
    }
}
