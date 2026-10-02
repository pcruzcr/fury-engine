namespace Fury.Core.Net;

/// <summary>
/// Transport port for the netcode: the sim and rollback layer only see this interface.
/// Concrete adapters (authenticated UDP, in-memory loopback for tests) implement it.
/// Inputs are exchanged as redundant windows with acks (see <see cref="InputPacket"/>):
/// loss is healed by the next datagram, never by blocking.
/// </summary>
public interface INetTransport
{
    /// <summary>
    /// Sends inputs of frames [<paramref name="firstFrame"/> .. firstFrame + bits.Length - 1]
    /// together with our ack of the peer inputs and a confirmed-state fingerprint
    /// (<paramref name="checkFrame"/>, <paramref name="checkHash"/>; -1 when none) for desync detection.
    /// Idempotent on retransmit.
    /// </summary>
    void SendInputs(int firstFrame, ReadOnlySpan<ushort> bits, int ackFrame, int checkFrame, ulong checkHash);

    /// <summary>Peer inputs received so far (thread-safe; also carries the peer's ack of our frames).</summary>
    RemoteInputBuffer Remote { get; }

    /// <summary>Smoothed round-trip estimate in ticks (1/60 s).</summary>
    int RttTicks { get; }
}
