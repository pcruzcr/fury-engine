// REFACTOR+HARDEN C6: branch-coverage gap closers (measured 83.97% → target 85%+).
// Every test below covers a REAL decoder/validator branch, not a tautology.
using Fighter.Core;

namespace Fighter.Core.Tests;

public sealed class CoverageGapTests
{
    [Fact]
    public void GimmickData_AllWords_RoundTrip()
    {
        var g = default(GimmickData);
        for (int k = 0; k < GimmickData.PayloadWords; k++)
            g.SetWord(k, 0x1000UL + (ulong)k);
        g.Tag = 9;
        for (int k = 0; k < GimmickData.PayloadWords; k++)
            Assert.Equal(0x1000UL + (ulong)k, g.GetWord(k));
        Assert.Equal(9, g.Tag);
        var other = default(GimmickData);
        Assert.False(g.Equals(other));
        Assert.False(g.Equals((object)other));
        _ = g.GetHashCode();
        Assert.True(default(GimmickData).Equals(default(GimmickData)));
    }

    [Fact]
    public void Vector2Fixed_FullContract()
    {
        var v = Vector2Fixed.FromInts(2, 3);
        Assert.True(v != Vector2Fixed.FromInts(2, 4));
        Assert.False(v.Equals((object)"nope"));
        Assert.True(v.Equals((object)Vector2Fixed.FromInts(2, 3)));
        _ = v.GetHashCode();
        Assert.Equal("(2.00000,3.00000)", v.ToString());
    }

    [Fact]
    public void Vector2I_FullContract()
    {
        var v = new Vector2I(3, -4);
        Assert.True(v == new Vector2I(3, -4));
        Assert.True(v != new Vector2I(3, -5));
        Assert.False(v.Equals((object)42));
        _ = v.GetHashCode();
        Assert.Equal("(3,-4)", v.ToString());
        Assert.Equal(new Vector2I(6, -8), v * 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputState(Buttons.None, 2, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputState(Buttons.None, 0, -2, 0));
    }

    [Fact]
    public void InputState_FullContract()
    {
        var a = InputState.FromBooleans(true, true, true, true, true, true, -1, 1, 9);
        Assert.Equal(Buttons.LP | Buttons.MP | Buttons.HP | Buttons.LK | Buttons.MK | Buttons.HK, a.Buttons);
        var b = new InputState(Buttons.LP, 0, 0, 0);
        Assert.True(a != b);
        Assert.True(b == new InputState(Buttons.LP, 0, 0, 0));
        Assert.False(b.Equals((object)"x"));
        Assert.True(b.Equals((object)new InputState(Buttons.LP, 0, 0, 0)));
        _ = b.GetHashCode();
    }

    private static byte[] Retampered(byte[] src, int offset, byte value)
    {
        // Tamper one header/body byte, then RECOMPUTE the footer so the failure
        // under test is the targeted validator, not the footer gate.
        byte[] copy = (byte[])src.Clone();
        copy[offset] = value;
        ulong footer = DeterminismHash.Hash(new System.ReadOnlySpan<byte>(copy, 0, copy.Length - 8));
        for (int k = 0; k < 8; k++) copy[copy.Length - 8 + k] = (byte)(footer >> (8 * k));
        return copy;
    }

    private static byte[] ValidV2Bytes()
    {
        var live = new FightSimulation(0xC0, GlenData.Build(), KiraData.Build());
        var initial = live.Capture();
        var p1 = new[] { new InputState(Buttons.LP, 0, 0, 0) };
        var p2 = new[] { new InputState(Buttons.None, 0, 0, 0) };
        return ReplayV2.SaveBytes(0xC0, StageData.ArenaNeutral, ReplayCast.Glen, ReplayCast.Kira,
            initial, p1, p2, ReplayPlatform.Linux64);
    }

    private static int SnapLen(byte[] bytes) =>
        bytes[62] | (bytes[63] << 8) | (bytes[64] << 16) | (bytes[65] << 24);

    [Fact]
    public void ReplayV2_CompressionFlagRejected()
    {
        byte[] bad = Retampered(ValidV2Bytes(), 57, 1);
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bad));
    }

    [Fact]
    public void ReplayV2_UnknownPlatformRejected()
    {
        byte[] bad = Retampered(ValidV2Bytes(), 56, 99);
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bad));
    }

    [Fact]
    public void ReplayV2_EmptyGameVersionRejected()
    {
        byte[] clean = ValidV2Bytes();
        byte[] copy = (byte[])clean.Clone();
        for (int i = 5; i < 21; i++) copy[i] = 0;
        ulong footer = DeterminismHash.Hash(new System.ReadOnlySpan<byte>(copy, 0, copy.Length - 8));
        for (int k = 0; k < 8; k++) copy[copy.Length - 8 + k] = (byte)(footer >> (8 * k));
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(copy));
    }

    [Fact]
    public void ReplayV2_ReservedByteRejected()
    {
        byte[] clean = ValidV2Bytes();
        int frame0 = 66 + SnapLen(clean);
        byte[] bad = Retampered(clean, frame0 + 3, 7);
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bad));
    }

    [Fact]
    public void ReplayV2_UnknownButtonsRejected()
    {
        byte[] clean = ValidV2Bytes();
        int frame0 = 66 + SnapLen(clean);
        byte[] bad = Retampered(clean, frame0, 0xFF);
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bad));
    }

    [Fact]
    public void ReplayV2_AxisOutOfRangeRejected()
    {
        byte[] clean = ValidV2Bytes();
        int frame0 = 66 + SnapLen(clean);
        byte[] bad = Retampered(clean, frame0 + 1, 5);
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(bad));
    }

    [Fact]
    public void ReplayV2_NullAndShortInputsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ReplayV2.LoadBytes(null!));
        Assert.Throws<ReplayCorruptException>(() => ReplayV2.LoadBytes(new byte[] { 1, 2, 3 }));
        var live = new FightSimulation(1, GlenData.Build(), GlenData.Build());
        Assert.Throws<ArgumentNullException>(() => ReplayV2.SaveBytes(1, 0, 1, 1, live.Capture(), null!, new InputState[1]));
        Assert.Throws<ArgumentNullException>(() => ReplayV2.SaveBytes(1, 0, 1, 1, null!, new InputState[1], new InputState[1]));
    }
}
