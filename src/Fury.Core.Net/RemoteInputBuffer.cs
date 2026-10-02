namespace Fury.Core.Net;

/// <summary>
/// Thread-safe, fixed-size store of the peer's inputs keyed by frame. Filled by the receive
/// thread, drained by the game thread. Bounded by construction: frames further ahead than
/// <see cref="Capacity"/> of the contiguous prefix are rejected, so a hostile or buggy peer
/// cannot grow memory (the old unbounded dictionary could be inflated with arbitrary frames).
/// Also tracks the peer's ack of OUR frames, which drives the redundancy window.
/// </summary>
public sealed class RemoteInputBuffer
{
    public const int Capacity = 128;

    private readonly object _gate = new();
    private readonly ushort[] _bits = new ushort[Capacity];
    private readonly int[] _frames = new int[Capacity];
    private int _contiguous = -1;
    private int _peerAck = -1;
    private int _peerCheckFrame = -1;
    private ulong _peerCheckHash;

    public RemoteInputBuffer() => Array.Fill(_frames, -1);

    /// <summary>Highest frame N such that peer inputs 0..N have all arrived (-1 if none): our ack.</summary>
    public int ContiguousThrough
    {
        get { lock (_gate) { return _contiguous; } }
    }

    /// <summary>Highest of OUR frames the peer reports having received contiguously (-1 if none).</summary>
    public int PeerAck
    {
        get { lock (_gate) { return _peerAck; } }
    }

    /// <summary>Stores a window of peer inputs and the peer's ack. Idempotent for duplicates.</summary>
    /// <returns>True when the ack advanced (an RTT sample is available).</returns>
    public bool Accept(int firstFrame, ReadOnlySpan<ushort> bits, int ackFrame, int checkFrame = -1, ulong checkHash = 0)
    {
        lock (_gate)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                int frame = firstFrame + i;
                if (frame < 0 || frame > _contiguous + Capacity) continue; // beyond the bounded window
                int slot = frame % Capacity;
                _frames[slot] = frame;
                _bits[slot] = bits[i];
            }

            while (_frames[(_contiguous + 1) % Capacity] == _contiguous + 1) _contiguous++;

            if (checkFrame > _peerCheckFrame)
            {
                _peerCheckFrame = checkFrame;
                _peerCheckHash = checkHash;
            }

            if (ackFrame > _peerAck)
            {
                _peerAck = ackFrame;
                return true;
            }
            return false;
        }
    }

    /// <summary>Latest confirmed-state fingerprint reported by the peer (desync detection).</summary>
    public bool TryGetPeerCheck(out int frame, out ulong hash)
    {
        lock (_gate)
        {
            frame = _peerCheckFrame;
            hash = _peerCheckHash;
            return frame >= 0;
        }
    }

    /// <summary>Reads the peer input of a frame; false while it has not arrived.</summary>
    public bool TryGet(int frame, out ushort bits)
    {
        lock (_gate)
        {
            if (frame >= 0)
            {
                int slot = frame % Capacity;
                if (_frames[slot] == frame)
                {
                    bits = _bits[slot];
                    return true;
                }
            }
        }
        bits = 0;
        return false;
    }
}
