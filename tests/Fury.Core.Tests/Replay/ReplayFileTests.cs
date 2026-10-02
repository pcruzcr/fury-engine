using Fury.Core.Data;
using Fury.Core.Replay;
using Fury.Core.Sim;
using Xunit;

namespace Fury.Core.Tests.Replay;

public class ReplayFileTests
{
    [Fact]
    public void Append_Get_Roundtrips()
    {
        var replay = new ReplayFile();
        var p1 = new InputState(0x1234);
        var p2 = new InputState(0x5678);

        replay.Append(p1, p2);
        replay.Append(p1, p2);

        Assert.Equal(2, replay.FrameCount);

        var (gotP1, gotP2) = replay.Get(0);
        Assert.Equal(p1, gotP1);
        Assert.Equal(p2, gotP2);

        (gotP1, gotP2) = replay.Get(1);
        Assert.Equal(p1, gotP1);
        Assert.Equal(p2, gotP2);
    }

    [Fact]
    public void ComputeDigest_IsDeterministic()
    {
        var replay1 = new ReplayFile();
        var replay2 = new ReplayFile();

        for (int i = 0; i < 10; i++)
        {
            replay1.Append(new InputState((ushort)i), new InputState((ushort)(i * 2)));
            replay2.Append(new InputState((ushort)i), new InputState((ushort)(i * 2)));
        }

        Assert.Equal(replay1.ComputeDigest(), replay2.ComputeDigest());
    }

    [Fact]
    public void ComputeDigest_DetectsTampering()
    {
        var replay1 = new ReplayFile();
        var replay2 = new ReplayFile();

        for (int i = 0; i < 10; i++)
        {
            replay1.Append(new InputState((ushort)i), new InputState((ushort)(i * 2)));
            replay2.Append(new InputState((ushort)i), new InputState((ushort)(i * 2)));
        }

        replay2.Append(new InputState(0xFFFF), new InputState(0xFFFF));

        Assert.NotEqual(replay1.ComputeDigest(), replay2.ComputeDigest());
    }

    [Fact]
    public void Get_OutOfRange_Throws()
    {
        var replay = new ReplayFile();
        replay.Append(new InputState(1), new InputState(2));

        Assert.Throws<ArgumentOutOfRangeException>(() => replay.Get(1));
    }
}

public class ReplaySerializationTests
{
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    private static (ReplayFile Replay, Func<Fury.Core.Sim.FightSimulation> Factory) RecordMatch(int frames = 300)
    {
        var cfg = FightConfig.Default();
        Fury.Core.Sim.FightSimulation Make() => new(GlenData.Create(), KiraData.Create(), cfg);
        var sim = Make();
        var recorder = new ReplayRecorder(sim, ReplayHeader.For(GlenData.Create(), KiraData.Create(), cfg, seed: 7));
        var rng = new Fury.Core.Math.Xoshiro256StarStar(123);
        for (int f = 0; f < frames; f++)
        {
            recorder.Step(new InputState((ushort)(rng.NextULong() & 0xFF)), new InputState((ushort)(rng.NextULong() & 0xFF)));
        }
        return (recorder.Finish(), Make);
    }

    [Fact]
    public void RoundTrip_PreservesEverything_AndVerifiesByResimulation()
    {
        var (replay, factory) = RecordMatch();
        var parsed = ReplayFile.Parse(replay.ToBytes());

        Assert.Equal(replay.FrameCount, parsed.FrameCount);
        Assert.Equal(replay.Header, parsed.Header);
        Assert.Equal(replay.FinalHash, parsed.FinalHash);
        Assert.Equal(replay.Checkpoints, parsed.Checkpoints);
        Assert.Equal(replay.ComputeDigest(), parsed.ComputeDigest());
        Assert.Equal(300 / ReplayFile.CheckpointInterval, parsed.Checkpoints.Count);
        Assert.True(parsed.Verify(factory).Valid);
    }

    [Fact]
    public void Verify_CatchesEditedInputs_EvenWhenTheTrailerIsRecomputed()
    {
        var (replay, factory) = RecordMatch();
        var forged = new ReplayFile(replay.Header);
        for (int f = 0; f < replay.FrameCount; f++)
        {
            var (a, b) = replay.Get(f);
            forged.Append(f is >= 40 and < 60 ? new InputState((ushort)(a.Bits ^ 0x0F)) : a, b);
        }
        foreach (var (frame, hash) in replay.Checkpoints) forged.AddCheckpoint(frame, hash);
        forged.Seal(replay.FinalHash);

        var reparsed = ReplayFile.Parse(forged.ToBytes()); // trailer is valid: only resimulation can tell
        var result = reparsed.Verify(factory);
        Assert.False(result.Valid);
        Assert.True(result.FailedFrame >= 40);
    }

    [Fact]
    public void EveryFlippedByte_IsRejected()
    {
        var (replay, _) = RecordMatch(frames: 70);
        byte[] bytes = replay.ToBytes();
        for (int i = 0; i < bytes.Length; i++)
        {
            var copy = (byte[])bytes.Clone();
            copy[i] ^= 0x01;
            Assert.Throws<InvalidDataException>(() => ReplayFile.Parse(copy));
        }
    }

    [Fact]
    public void Truncations_AndGarbage_AreRejected_WithoutCrashing()
    {
        var (replay, _) = RecordMatch(frames: 70);
        byte[] bytes = replay.ToBytes();
        for (int len = 0; len < bytes.Length; len++)
        {
            Assert.Throws<InvalidDataException>(() => ReplayFile.Parse(bytes.AsSpan(0, len)));
        }

        var rng = new Random(5);
        for (int i = 0; i < 200; i++)
        {
            var junk = new byte[rng.Next(0, 300)];
            rng.NextBytes(junk);
            Assert.Throws<InvalidDataException>(() => ReplayFile.Parse(junk));
        }
    }

    [Fact]
    public void HostileFrameCount_IsRejectedBeforeAllocating()
    {
        var (replay, _) = RecordMatch(frames: 70);
        byte[] bytes = replay.ToBytes();
        // frameCount sits after magic(4)+words(2)+mode(1)+4 hashes(32)+two id fields.
        int idBytes = 1 + replay.Header.P1Id.Length + 1 + replay.Header.P2Id.Length;
        int frameCountOffset = 4 + 2 + 1 + 32 + idBytes;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(frameCountOffset), int.MaxValue);
        // Re-seal so only the structural check can reject it.
        System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32), bytes.AsSpan(bytes.Length - 32));
        Assert.Throws<InvalidDataException>(() => ReplayFile.Parse(bytes));
    }

    [Fact]
    public void Hmac_RequiresTheKey_AndCannotBeDowngraded()
    {
        var (replay, _) = RecordMatch(frames: 70);
        byte[] signed = replay.ToBytes(Key);
        byte[] unsigned = replay.ToBytes();

        Assert.Equal(70, ReplayFile.Parse(signed, Key).FrameCount);
        Assert.Throws<InvalidDataException>(() => ReplayFile.Parse(signed, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<InvalidDataException>(() => ReplayFile.Parse(signed));          // key required
        Assert.Throws<InvalidDataException>(() => ReplayFile.Parse(unsigned, Key));   // downgrade attempt
    }

    [Fact]
    public void Header_FingerprintsDetectDifferentRulesOrCharacters()
    {
        var cfg = FightConfig.Default();
        var a = ReplayHeader.For(GlenData.Create(), KiraData.Create(), cfg);
        Assert.Equal(a, ReplayHeader.For(GlenData.Create(), KiraData.Create(), cfg));
        Assert.NotEqual(a.ConfigHash, ReplayHeader.For(GlenData.Create(), KiraData.Create(), cfg with { ChipDamagePercent = 11 }).ConfigHash);
        Assert.NotEqual(a.P1Hash, a.P2Hash);
        Assert.NotEqual(a.P1Hash, ReplayHeader.For(KiraData.Create(), GlenData.Create(), cfg).P1Hash);
    }
}
