// REVIEW contribution (parallel-work integration): pins the invariant that the
// recorded Phase-4 intent (CommandNormalResolver, informational) never contradicts
// the fight authority (MoveResolver) for button/command-normal starts.
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class IntentAuthorityParityTests
{
    private static (Buttons Buttons, DirReq Dir) Expect(ResolvedInput r) => r switch
    {
        ResolvedInput.NeutralLP => (Buttons.LP, DirReq.Neutral),
        ResolvedInput.NeutralMP => (Buttons.MP, DirReq.Neutral),
        ResolvedInput.NeutralHP => (Buttons.HP, DirReq.Neutral),
        ResolvedInput.NeutralLK => (Buttons.LK, DirReq.Neutral),
        ResolvedInput.NeutralMK => (Buttons.MK, DirReq.Neutral),
        ResolvedInput.NeutralHK => (Buttons.HK, DirReq.Neutral),
        ResolvedInput.ForwardHP => (Buttons.HP, DirReq.Forward),
        ResolvedInput.BackHP => (Buttons.HP, DirReq.Back),
        ResolvedInput.ForwardMP => (Buttons.MP, DirReq.Forward),
        ResolvedInput.BackMP => (Buttons.MP, DirReq.Back),
        ResolvedInput.BackLP => (Buttons.LP, DirReq.Back),
        ResolvedInput.ForwardHK => (Buttons.HK, DirReq.Forward),
        ResolvedInput.BackHK => (Buttons.HK, DirReq.Back),
        ResolvedInput.ForwardLK => (Buttons.LK, DirReq.Forward),
        ResolvedInput.BackLK => (Buttons.LK, DirReq.Back),
        ResolvedInput.DownBackLK => (Buttons.LK, DirReq.DownBack),
        _ => (Buttons.None, DirReq.Neutral),
    };

    [Fact]
    public void RecordedIntent_MatchesStartedButtonMoves()
    {
        var s = New(777);
        WalkIntoRange(s);
        // (buttons, dx, dy) → expected Glen move id. Neutrals between let each move finish,
        // so every tap is a fresh start (whiffs allowed: starts don't need range).
        var taps = new (Buttons b, int dx, int dy, int expect)[]
        {
            (Buttons.LP, 0, 0, GlenData.LP), (Buttons.HP, 1, 0, GlenData.F_HP),
            (Buttons.HP, -1, 0, GlenData.B_HP), (Buttons.MP, 1, 0, GlenData.F_MP),
            (Buttons.MP, -1, 0, GlenData.B_MP), (Buttons.LP, -1, 0, GlenData.B_LP),
            (Buttons.HK, 1, 0, GlenData.F_HK), (Buttons.HK, -1, 0, GlenData.B_HK),
            (Buttons.LK, 1, 0, GlenData.F_LK), (Buttons.LK, -1, 0, GlenData.B_LK),
            (Buttons.LK, -1, -1, GlenData.Sweep), (Buttons.MK, 0, 0, GlenData.MK),
        };
        foreach (var (b, dx, dy, expect) in taps)
        {
            for (int k = 0; k < 60 && s.Fighters[0].State != FighterState.Neutral; k++) Neutrals(s, 1);
            Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
            Tap(s, 0, b, dx, dy);
            var f = s.Fighters[0];
            Assert.Equal(expect, f.MoveId);
            var move = Glen.Get(f.MoveId);
            var (eb, edir) = Expect((ResolvedInput)f.LastCommandNormal);
            Assert.Equal(move.Buttons, eb);
            Assert.Equal(move.Dir, edir);
            Neutrals(s, 1);
        }
    }

    [Fact]
    public void IntentRecorded_EvenWhileFrozen()
    {
        var s = New(778);
        WalkIntoRange(s);
        Neutrals(s, 8); // flush WalkIntoRange's forward-hold out of the 8-ring:
        // otherwise the 10f direction window legitimately reports ForwardMP.
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 20 && !s.Fighters[0].DidHit; k++) Neutrals(s, 1);
        // During LP's hitstop freeze, tap MP: intent must record despite no state change allowed.
        Tap(s, 0, Buttons.MP);
        Assert.Equal((int)ResolvedInput.NeutralMP, s.Fighters[0].LastCommandNormal);
    }
}
