using System.Security.Cryptography;
using System.Text;
using Fury.Core.Net;
using Xunit;

namespace Fury.Core.Tests.Net;

public class PacketAuthenticatorTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Stamp_Verify_Roundtrips()
    {
        using var auth = new PacketAuthenticator(NewKey());
        var payload = Encoding.UTF8.GetBytes("frame inputs");
        var packet = new byte[PacketAuthenticator.EncodedLength(payload.Length)];

        auth.Stamp(payload, sequence: 1, packet);
        var outPayload = new byte[payload.Length];

        Assert.True(auth.Verify(packet, outPayload, out uint seq));
        Assert.Equal(1u, seq);
        Assert.Equal(payload, outPayload);
    }

    [Fact]
    public void TamperedByte_IsRejected()
    {
        using var auth = new PacketAuthenticator(NewKey());
        var payload = new byte[] { 1, 2, 3, 4 };
        var packet = new byte[PacketAuthenticator.EncodedLength(payload.Length)];
        auth.Stamp(payload, 1, packet);

        packet[1] ^= 0xFF;
        Assert.False(auth.Verify(packet, new byte[payload.Length], out _));

        packet[1] ^= 0xFF;
        packet[^1] ^= 0x01;
        Assert.False(auth.Verify(packet, new byte[payload.Length], out _));
    }

    [Fact]
    public void TruncatedPacket_IsRejected()
    {
        using var auth = new PacketAuthenticator(NewKey());
        Assert.False(auth.Verify(new byte[10], new byte[10], out _));
    }

    [Fact]
    public void KeyShorterThan256Bits_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PacketAuthenticator(new byte[16]));
    }

    [Fact]
    public void ReplayGuard_DropsDuplicatesAndOldPackets()
    {
        var guard = new PacketReplayGuard();
        Assert.True(guard.Accept(1));
        Assert.False(guard.Accept(1));
        Assert.True(guard.Accept(2));
        Assert.True(guard.Accept(5));
        Assert.False(guard.Accept(2));
        Assert.True(guard.Accept(80));
        Assert.False(guard.Accept(1));
        Assert.False(guard.Accept(0));
    }
}
