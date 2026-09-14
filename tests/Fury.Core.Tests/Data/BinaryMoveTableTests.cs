using Fury.Core.Sim;
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
        PushbackRaw: 0x28000, // Fp 2.5
        ReachRaw: 0x60000,    // Fp 6.0
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
        Assert.True(parsed.TryGetByNameHash(42, out var byName));
        Assert.Equal(120, byName.Damage);
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
    public void TryGetByNameHash_Miss_ReturnsFalse()
    {
        var table = BinaryMoveTable.Parse(BinaryMoveTableWriter.Write([Sample]));
        Assert.False(table.TryGetByNameHash(-1, out _));
    }
}
