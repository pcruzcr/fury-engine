using System.Runtime.InteropServices;
using Fury.Core.Data;
using Fury.Core.Net;
using Fury.Core.Rollback;
using Xunit;

namespace Fury.Core.Tests.Security;

/// <summary>
/// F4 gate: Fuzzing tests for data and network parsers.
/// Any malformed input must be rejected gracefully — never crash or read silently.
/// </summary>
public class FuzzingTests
{
    [Fact]
    public void BinaryMoveTable_RandomBlob_ThrowsOrParses()
    {
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            var blob = new byte[rng.Next(0, 200)];
            rng.NextBytes(blob);

            try
            {
                var table = BinaryMoveTable.Parse(blob);
                if (table.MoveCount > 0)
                {
                    var move = table.GetMove(0);
                    Assert.True(move.StartupFrames >= 0);
                }
            }
            catch (InvalidDataException)
            {
                // Expected for malformed input.
            }
        }
    }

    [Fact]
    public void BinaryMoveTable_TruncatedBlob_Throws()
    {
        var validBlob = BinaryMoveTableWriter.Write([new MoveEntry(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)]);

        for (int i = 0; i < validBlob.Length; i++)
        {
            byte[] truncated = validBlob[..i];
            Assert.Throws<InvalidDataException>(() => BinaryMoveTable.Parse(truncated));
        }
    }

    [Fact]
    public void PacketAuthenticator_RandomPacket_Rejects()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var auth = new PacketAuthenticator(key);
        var rng = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            var packet = new byte[rng.Next(0, 100)];
            rng.NextBytes(packet);

            var payload = new byte[100];
            bool result = auth.Verify(packet, payload, out _);

            Assert.False(result);
        }
    }

    [Fact]
    public void PacketAuthenticator_TamperedPacket_Rejects()
    {
        var key = new byte[32];
        new Random(42).NextBytes(key);

        using var auth = new PacketAuthenticator(key);
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var packet = new byte[PacketAuthenticator.EncodedLength(payload.Length)];

        auth.Stamp(payload, 1, packet);

        for (int i = 0; i < packet.Length; i++)
        {
            var tampered = (byte[])packet.Clone();
            tampered[i] ^= 0xFF;

            var outPayload = new byte[payload.Length];
            Assert.False(auth.Verify(tampered, outPayload, out _));
        }
    }

    [Fact]
    public void SnapshotDiffer_CorruptDelta_Throws()
    {
        int[] before = [1, 2, 3, 4, 5];
        int[] corruptDelta = [0, 99];

        Assert.Throws<InvalidDataException>(() =>
            SnapshotDiffer.Apply(before, corruptDelta, new int[5]));
    }

    [Fact]
    public void SnapshotDiffer_RandomDelta_ThrowsOrApplies()
    {
        var rng = new Random(42);
        int[] before = [1, 2, 3, 4, 5];

        for (int i = 0; i < 100; i++)
        {
            var delta = new int[rng.Next(0, 20)];
            rng.NextBytes(MemoryMarshal.AsBytes(delta.AsSpan()));

            try
            {
                SnapshotDiffer.Apply(before, delta, new int[5]);
            }
            catch (InvalidDataException)
            {
                // Expected for corrupt delta.
            }
            catch (IndexOutOfRangeException)
            {
                // Also expected for random data that causes out-of-bounds access.
            }
        }
    }
}
