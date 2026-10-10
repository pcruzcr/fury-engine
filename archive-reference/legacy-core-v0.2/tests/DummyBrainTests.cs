// Dummy brain contract tests (modes are exact by construction).
using Fighter.Core;
using static Fighter.Core.Tests.FightKit;

namespace Fighter.Core.Tests;

public sealed class DummyBrainTests
{
    [Fact]
    public void Stand_IsAlwaysNeutral()
    {
        var s = NewVsKira(9301);
        WalkIntoRange(s);
        for (int f = 0; f < 120; f++)
        {
            var inp = DummyBrain.For(s, s.Frame, DummyMode.Stand);
            Assert.Equal(Buttons.None, inp.Buttons);
            Assert.Equal(0, inp.DirX);
            s.Step(new InputState(Buttons.None, 0, 0, s.Frame), inp);
        }
    }

    [Fact]
    public void Block_HoldsAway()
    {
        var s = NewVsKira(9302);
        WalkIntoRange(s);
        for (int f = 0; f < 30; f++)
        {
            var inp = DummyBrain.For(s, s.Frame, DummyMode.Block);
            Assert.Equal(-s.Fighters[1].Facing, inp.DirX);
            s.Step(new InputState(Buttons.None, 0, 0, s.Frame), inp);
        }
    }

    [Fact]
    public void Jump_HopsPeriodically()
    {
        var s = NewVsKira(9303);
        WalkIntoRange(s);
        bool jumped = false;
        for (int f = 0; f < 200; f++)
        {
            var inp = DummyBrain.For(s, s.Frame, DummyMode.Jump);
            s.Step(new InputState(Buttons.None, 0, 0, s.Frame), inp);
            if (s.Fighters[1].Airborne) jumped = true;
        }
        Assert.True(jumped);
    }

    [Fact]
    public void InputLatency_EdgeAppliesNextStep()
    {
        // Architectural latency bound: a fresh button edge starts its move on the
        // very next Step (input→sim ≤ 1 frame by construction, never by feel).
        var s = New(9304);
        WalkIntoRange(s);
        s.Step(new InputState(Buttons.LP, 0, 0, s.Frame), new InputState(Buttons.None, 0, 0, s.Frame));
        Assert.Equal(GlenData.LP, s.Fighters[0].MoveId);
    }
}
