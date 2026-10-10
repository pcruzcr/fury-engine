// REFACTOR+HARDEN A5/D9: CommandNormalResolver coverage (every GDD variant,
// window boundaries 9f/10f/11f, neutral fallback, chords, states).
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class CommandNormalResolverTests
{
    // Builds a hold-direction-then-edge buffer: `hold` frames of (dir, no buttons)
    // followed by one edge frame (dir, button). Total length = hold + 1.
    private static InputState[] Buffer(int hold, int dirX, int dirY, Buttons edge, int facing)
    {
        var buf = new InputState[hold + 1];
        for (int i = 0; i < hold; i++)
            buf[i] = new InputState(Buttons.None, dirX, dirY, i);
        buf[hold] = new InputState(edge, dirX, dirY, hold);
        return buf;
    }

    private static ResolvedInput ResolveAt(InputState[] buf, int facing) =>
        CommandNormalResolver.Resolve(buf, FighterState.Neutral, facing);

    [Theory]
    [InlineData(Buttons.HP, 1, 0, ResolvedInput.ForwardHP)]
    [InlineData(Buttons.MP, 1, 0, ResolvedInput.ForwardMP)]
    [InlineData(Buttons.HK, 1, 0, ResolvedInput.ForwardHK)]
    [InlineData(Buttons.LK, 1, 0, ResolvedInput.ForwardLK)]
    [InlineData(Buttons.HP, -1, 0, ResolvedInput.BackHP)]
    [InlineData(Buttons.MP, -1, 0, ResolvedInput.BackMP)]
    [InlineData(Buttons.LP, -1, 0, ResolvedInput.BackLP)]
    [InlineData(Buttons.HK, -1, 0, ResolvedInput.BackHK)]
    [InlineData(Buttons.LK, -1, 0, ResolvedInput.BackLK)]
    [InlineData(Buttons.LK, -1, -1, ResolvedInput.DownBackLK)]
    public void AllVariants_Resolve_WithHeldDirection(Buttons btn, int dir, int dy, ResolvedInput expected)
    {
        // Facing +1: screen +X is forward. Back variants use screen -X.
        var buf = Buffer(hold: 3, dir, dy, btn, facing: 1);
        Assert.Equal(expected, ResolveAt(buf, facing: 1));
    }

    [Fact]
    public void FacingMinusOne_MirrorsDirections()
    {
        var buf = Buffer(hold: 3, dirX: -1, dirY: 0, Buttons.HP, facing: -1);
        Assert.Equal(ResolvedInput.ForwardHP, ResolveAt(buf, facing: -1));
        var buf2 = Buffer(hold: 3, dirX: 1, dirY: 0, Buttons.HP, facing: -1);
        Assert.Equal(ResolvedInput.BackHP, ResolveAt(buf2, facing: -1));
    }

    [Theory]
    [InlineData(8, ResolvedInput.ForwardHP)]  // direction 9 entries back: oldest scanned entry, inside
    [InlineData(9, ResolvedInput.NeutralHP)] // direction 10 entries back: cut off -> fallback
    public void WindowBoundary_EdgeCases(int neutralGap, ResolvedInput expected)
    {
        // One forward frame, then neutralGap neutral frames, then HP edge.
        var buf = new System.Collections.Generic.List<InputState>
        {
            new(Buttons.None, 1, 0, 0),
        };
        for (int i = 1; i <= neutralGap; i++)
            buf.Add(new InputState(Buttons.None, 0, 0, i));
        buf.Add(new InputState(Buttons.HP, 0, 0, neutralGap + 1));
        Assert.Equal(expected, CommandNormalResolver.Resolve(
            new System.ReadOnlySpan<InputState>(buf.ToArray()), FighterState.Neutral, 1));
    }

    [Fact]
    public void WindowExactly10Entries_StillCounts()
    {
        // Direction on the OLDEST of 10 scanned entries (9 newer entries): inside.
        var buf = new InputState[10];
        buf[0] = new InputState(Buttons.None, 1, 0, 0);
        for (int i = 1; i < 9; i++) buf[i] = new InputState(Buttons.None, 0, 0, i);
        buf[9] = new InputState(Buttons.HP, 0, 0, 9);
        Assert.Equal(ResolvedInput.ForwardHP,
            CommandNormalResolver.Resolve(buf, FighterState.Neutral, 1));
    }

    [Fact]
    public void Window11Entries_OldestDropsOut()
    {
        // 11 entries: scanner sees only the last 10; the forward frame is cut.
        var buf = new InputState[11];
        buf[0] = new InputState(Buttons.None, 1, 0, 0);
        for (int i = 1; i < 10; i++) buf[i] = new InputState(Buttons.None, 0, 0, i);
        buf[10] = new InputState(Buttons.HP, 0, 0, 10);
        Assert.Equal(ResolvedInput.NeutralHP,
            CommandNormalResolver.Resolve(buf, FighterState.Neutral, 1));
    }

    [Theory]
    [InlineData(Buttons.LP, ResolvedInput.NeutralLP)]
    [InlineData(Buttons.MP, ResolvedInput.NeutralMP)]
    [InlineData(Buttons.HP, ResolvedInput.NeutralHP)]
    [InlineData(Buttons.LK, ResolvedInput.NeutralLK)]
    [InlineData(Buttons.MK, ResolvedInput.NeutralMK)]
    [InlineData(Buttons.HK, ResolvedInput.NeutralHK)]
    public void NeutralFallback_PerButton(Buttons btn, ResolvedInput expected)
    {
        var buf = Buffer(hold: 2, dirX: 0, dirY: 0, btn, facing: 1);
        Assert.Equal(expected, ResolveAt(buf, facing: 1));
    }

    [Fact]
    public void NonCommandCombos_FallBackToNeutral()
    {
        // F+LP, F+MK, B+MK have no GDD command normal: neutral fallback.
        Assert.Equal(ResolvedInput.NeutralLP, ResolveAt(Buffer(2, 1, 0, Buttons.LP, 1), 1));
        Assert.Equal(ResolvedInput.NeutralMK, ResolveAt(Buffer(2, 1, 0, Buttons.MK, 1), 1));
        Assert.Equal(ResolvedInput.NeutralMK, ResolveAt(Buffer(2, -1, 0, Buttons.MK, 1), 1));
        // Down-back + HP counts as back (only LK owns the down-back variant).
        Assert.Equal(ResolvedInput.BackHP, ResolveAt(Buffer(2, -1, -1, Buttons.HP, 1), 1));
    }

    [Fact]
    public void DownBackLK_BeatsBackLK_Priority()
    {
        // Most recent decisive direction wins: back held, then down-back, then edge.
        var buf = new[]
        {
            new InputState(Buttons.None, -1, 0, 0),
            new InputState(Buttons.None, -1, -1, 1),
            new InputState(Buttons.LK, -1, -1, 2),
        };
        Assert.Equal(ResolvedInput.DownBackLK,
            CommandNormalResolver.Resolve(buf, FighterState.Neutral, 1));
    }

    [Fact]
    public void ChordEdge_ResolvesNone()
    {
        var buf = Buffer(hold: 2, dirX: 1, dirY: 0, Buttons.LP | Buttons.LK, facing: 1);
        Assert.Equal(ResolvedInput.None, ResolveAt(buf, facing: 1));
    }

    [Fact]
    public void NoEdge_ResolvesNone()
    {
        var buf = new[]
        {
            new InputState(Buttons.HP, 1, 0, 0),
            new InputState(Buttons.HP, 1, 0, 1), // held, no fresh edge
        };
        Assert.Equal(ResolvedInput.None, ResolveAt(buf, facing: 1));
        Assert.Equal(ResolvedInput.None,
            CommandNormalResolver.Resolve(System.ReadOnlySpan<InputState>.Empty, FighterState.Neutral, 1));
    }

    [Theory]
    [InlineData(FighterState.KO)]
    [InlineData(FighterState.Knockdown)]
    [InlineData(FighterState.Wakeup)]
    public void NonParticipatingStates_ResolveNone(FighterState state)
    {
        var buf = Buffer(hold: 2, dirX: 1, dirY: 0, Buttons.HP, facing: 1);
        Assert.Equal(ResolvedInput.None, CommandNormalResolver.Resolve(buf, state, 1));
    }

    [Fact]
    public void BadFacing_Throws()
    {
        var buf = Buffer(hold: 1, dirX: 0, dirY: 0, Buttons.HP, facing: 1);
        Assert.Throws<ArgumentException>(() => ResolveAt(buf, facing: 0));
    }

    [Fact]
    public void LiveSim_RecordsIntent_InSnapshot()
    {
        // Phase-4 wiring proof: forward held + HP edge => LastCommandNormal set.
        var s = FightKit.New(77);
        for (int k = 0; k < 3; k++)
            FightKit.T(s, Buttons.None, 1, 0, Buttons.None, 0, 0);
        FightKit.T(s, Buttons.HP, 1, 0, Buttons.None, 0, 0);
        Assert.Equal((int)ResolvedInput.ForwardHP, s.Fighters[0].LastCommandNormal);
        // Restore preserves the recorded intent (schema-5 coverage).
        var snap = s.Capture();
        var resumed = FightKit.New(999);
        resumed.Restore(snap);
        Assert.Equal(s.Fighters[0].LastCommandNormal, resumed.Fighters[0].LastCommandNormal);
    }
}
