// §54 Test 3: replay exact reproduction (record → fresh sim → identical per-frame hashes).
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class ReplayTests
{
    private const ulong Seed = 0xC0FF_EE11_2233_4455UL;

    [Fact]
    public void RecordThenReplay_ReproducesEveryFrameHash()
    {
        const int frames = 600;
        var live = new DeterministicSim(Seed);
        var rec = new ReplayRecorder(Seed);
        var liveHashes = new ulong[frames];
        for (int f = 0; f < frames; f++)
        {
            var p1 = InputGen.P1(f);
            var p2 = InputGen.P2(f);
            live.Step(p1, p2);
            rec.Record(p1, p2);
            liveHashes[f] = live.Capture().Hash;
        }

        var player = ReplayRecorder.Load(rec.ToBytes());
        Assert.Equal(Seed, player.Seed);
        Assert.Equal(frames, player.Count);
        Assert.Equal(liveHashes, player.PlayAll());
    }

    [Fact]
    public void Replay_StepwiseMatchesLive()
    {
        const int frames = 120;
        var rec = new ReplayRecorder(Seed);
        for (int f = 0; f < frames; f++)
            rec.Record(InputGen.P1(f), InputGen.P2(f));
        var bytes = rec.ToBytes();

        // Size sanity: header + frames*(2 inputs*16B). Must stay tiny (target 20–40 KB/3min).
        Assert.True(bytes.Length < 64 * 1024, $"Replay too large: {bytes.Length}B for {frames} frames.");
    }

    [Fact]
    public void CorruptMagic_Throws()
    {
        var rec = new ReplayRecorder(Seed);
        rec.Record(InputGen.P1(0), InputGen.P2(0));
        byte[] bytes = rec.ToBytes();
        bytes[0] ^= 0xFF;
        Assert.Throws<ReplayCorruptException>(() => ReplayRecorder.Load(bytes));
    }
}
