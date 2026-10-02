using Fury.Core.Data;
using Xunit;

namespace Fury.Core.Tests.Data;

public class BinaryMoveTableTests
{
    private static readonly MoveEntry Sample = new(
        NameHash: unchecked((int)0x9E3779B9),
        StartupFrames: 5,
        ActiveFrames: 3,
        RecoveryFrames: 11,
        Damage: 80,
        HitstopFrames: 6,
        PushbackRaw: 0x28000,
        ReachRaw: 0x60000,
        BoxHalfHeightRaw: 0x28000,
        BoxCenterYRaw: 0x3C000);

    [Fact]
    public void Write_Parse_Roundtrips()
    {
        var moves = new[] { Sample, Sample with { NameHash = 42, Damage = 120 } };
        var table = BinaryMoveTableWriter.Write(moves);

        var parsed = BinaryMoveTable.Parse(table);
        Assert.Equal(2, parsed.MoveCount);
        Assert.Equal(Sample, parsed.GetMove(0));
        Assert.Equal(120, parsed.GetMove(1).Damage);
    }

    [Fact]
    public void Fmt2_Roundtrips_EveryExtendedField()
    {
        var move = Sample with
        {
            HitstunFrames = 17,
            BlockstunFrames = 9,
            Traits = MoveTraits.Air | MoveTraits.Launcher | MoveTraits.Knockdown,
            Level = HitLevel.Overhead,
            Command = 0x0201_0010,
            Category = 2,
            CancelMask = 0b1100,
            LaunchVyRaw = 0xA0000,
            MeterCost = 100,
            Priority = 7,
            ProjectileSpeedRaw = 0x30000,
            ProjectileLifetime = 90,
            ProjectileSpawnFrame = 12,
        };

        var parsed = BinaryMoveTable.Parse(BinaryMoveTableWriter.Write([move])).GetMove(0);

        Assert.Equal(move, parsed);
        Assert.Equal(17, parsed.EffectiveHitstun);
        Assert.Equal(9, parsed.EffectiveBlockstun);
        Assert.True(parsed.Has(MoveTraits.Launcher));
        Assert.False(parsed.Has(MoveTraits.Throw));
    }

    [Fact]
    public void Fmt1_LegacyBlob_IsReadableAndKeepsTheLegacyStunFormulas()
    {
        var blob = BinaryMoveTableWriter.WriteLegacy([Sample]);
        Assert.Equal((byte)(char)49, blob[3]); // "FMT1"

        var parsed = BinaryMoveTable.Parse(blob).GetMove(0);

        Assert.Equal(Sample, parsed);
        Assert.Equal(Sample.RecoveryFrames + 6, parsed.EffectiveHitstun);
        Assert.Equal((Sample.RecoveryFrames / 2) + 3, parsed.EffectiveBlockstun);
    }

    [Fact]
    public void Parse_RejectsMismatchedMagicAndVersion_AndHostileCounts()
    {
        var v2 = BinaryMoveTableWriter.Write([Sample]);
        var wrongPair = (byte[])v2.Clone();
        wrongPair[3] = (byte)(char)49; // FMT1 magic with a version-2 body
        Assert.Throws<InvalidDataException>(() => BinaryMoveTable.Parse(wrongPair));

        var hostile = (byte[])v2.Clone();
        BitConverter.TryWriteBytes(hostile.AsSpan(8), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => BinaryMoveTable.Parse(hostile));
    }

    [Fact]
    public void ContentHash_ChangesWithAnyField()
    {
        ulong Hash(MoveEntry m) => BinaryMoveTable.Parse(BinaryMoveTableWriter.Write([m])).ContentHash;
        ulong baseline = Hash(Sample);
        Assert.Equal(baseline, Hash(Sample));
        Assert.NotEqual(baseline, Hash(Sample with { Damage = 81 }));
        Assert.NotEqual(baseline, Hash(Sample with { HitstunFrames = 1 }));
        Assert.NotEqual(baseline, Hash(Sample with { Level = HitLevel.Low }));
    }

    [Fact]
    public void Parse_Truncated_Throws()
    {
        var blob = BinaryMoveTableWriter.Write([Sample]);
        Assert.Throws<InvalidDataException>(() => BinaryMoveTable.Parse(blob.AsSpan(0, blob.Length - 4)));
    }

    [Fact]
    public void Parse_BadMagic_Throws()
    {
        var blob = BinaryMoveTableWriter.Write([Sample]);
        blob[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => BinaryMoveTable.Parse(blob));
    }

    [Fact]
    public void Parse_BadVersion_Throws()
    {
        var blob = BinaryMoveTableWriter.Write([Sample]);
        blob[4] = 9;
        Assert.Throws<InvalidDataException>(() => BinaryMoveTable.Parse(blob));
    }

    [Fact]
    public void GetMove_OutOfRange_Throws()
    {
        var table = BinaryMoveTable.Parse(BinaryMoveTableWriter.Write([Sample]));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.GetMove(1));
    }
}
