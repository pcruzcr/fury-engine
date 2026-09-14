using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Sim;

public class FightClockTests
{
    [Fact]
    public void Tick_AdvancesOneFramePerCall()
    {
        var clock = new FightClock();
        var neutral = new InputState(0);
        Assert.Equal(0, clock.Frame);
        clock.Tick(neutral, neutral);
        clock.Tick(neutral, neutral);
        Assert.Equal(2, clock.Frame);
    }
}

public class RollbackSessionTests
{
    [Fact]
    public void Record_And_HashMatch()
    {
        var session = new Fury.Core.Rollback.RollbackSession();
        session.Record(0, new InputState(0b11), 0xDEADBEEFUL);
        Assert.True(session.HashMatches(0, 0xDEADBEEFUL));
        Assert.False(session.HashMatches(0, 0xCAFEBABEUL));
        Assert.Equal(0, session.ConfirmedFrame);
    }
}

public class ReplayFileTests
{
    [Fact]
    public void Digest_StableForSameInputs_TamperDetected()
    {
        var r = new Fury.Core.Replay.ReplayFile();
        r.Append(new InputState(1), new InputState(2));
        var d1 = r.ComputeDigest();
        var d2 = r.ComputeDigest();
        Assert.Equal(d1, d2);

        var other = new Fury.Core.Replay.ReplayFile();
        other.Append(new InputState(1), new InputState(3));
        Assert.NotEqual(d1, other.ComputeDigest());
    }
}
