// Round flow tests: NextRound resets, EX carry/grace, determinism across rounds.
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class RoundFlowTests
{
    [Fact]
    public void NextRound_ResetsPositionsTimersAndCombos_KeepsWinnerEx()
    {
        var s = New(901);
        WalkIntoRange(s);
        s.Fighters[0].Ex = 2000;
        s.Fighters[1].Hp = 10;
        Tap(s, 0, Buttons.LP);
        for (int k = 0; k < 40 && !s.Over; k++) Neutrals(s, 1);
        Assert.True(s.Over);
        // NOTE: Hp=10 arms the Resonance lifeline (OPEN-003): LP 28 → 22 final,
        // so the winner banks (22/5)*30 = 120, not 150. The mechanic is the proof.
        Assert.Equal(2120, s.Fighters[0].Ex);
        Assert.Equal(0, s.Winner);
        s.NextRound();
        Assert.False(s.Over);
        Assert.Equal(-1, s.Winner);
        Assert.Equal(2, s.RoundNumber);
        Assert.Equal(950, s.Fighters[0].Hp);
        Assert.Equal(950, s.Fighters[1].Hp);
        Assert.Equal(2120, s.Fighters[0].Ex); // winner keeps (2000 + 120 post-lifeline)
        Assert.Equal(900, s.Fighters[1].Ex);  // loser grace 30%
        Assert.Equal(300000, s.Fighters[0].Pos.X);
        Assert.Equal(660000, s.Fighters[1].Pos.X);
        Assert.Equal(FighterState.Neutral, s.Fighters[0].State);
        Assert.Equal(80 * 60, s.RoundTimer);
        Assert.Equal(0, s.Fighters[0].ComboGiven);
    }

    [Fact]
    public void NextRound_WithoutOver_Throws()
    {
        var s = New(902);
        Assert.Throws<System.InvalidOperationException>(() => s.NextRound());
    }

    [Fact]
    public void TwoRounds_DeterministicAcrossReset()
    {
        static ulong Run()
        {
            var s = New(903);
            WalkIntoRange(s);
            s.Fighters[1].Hp = 10;
            Tap(s, 0, Buttons.LP);
            for (int k = 0; k < 40 && !s.Over; k++) Neutrals(s, 1);
            Assert.True(s.Over);
            s.NextRound();
            for (int f = 0; f < 200; f++) FightDeterminismTests.Script(s, f);
            return s.Capture().Hash;
        }
        Assert.Equal(Run(), Run());
    }
}
