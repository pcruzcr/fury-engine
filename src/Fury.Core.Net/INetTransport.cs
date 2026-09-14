namespace Fury.Core.Net;

/// <summary>
/// Transport port for the netcode: the sim and rollback layer only see this interface.
/// Concrete adapters (UDP with HMAC session, loopback for tests) plug in from Fury.Game.
/// </summary>
public interface INetTransport
{
    /// <summary>Send a confirmed-input packet for a frame. Must be idempotent on retransmit.</summary>
    void SendInputs(int frame, ushort localBits);

    /// <summary>Try to dequeue remote inputs. Returns false while the frame is still predicted.</summary>
    bool TryReceiveInputs(int frame, out ushort remoteBits);

    /// <summary>Round-trip estimate in ticks (not ms) — keeps the whole stack deterministic-adjacent.</summary>
    int RttTicks { get; }
}
