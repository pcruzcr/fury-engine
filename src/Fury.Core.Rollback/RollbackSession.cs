using Fury.Core.Math;
using Fury.Core.Sim;

namespace Fury.Core.Rollback;

/// <summary>
/// Fixed-capacity ring of saved sim inputs + state hashes enabling prediction and
/// resimulation. F0 scaffolding; save/restore of full fight state lands in F2.
/// </summary>
public sealed class RollbackSession
{
    private readonly InputState[] _inputs;
    private readonly ulong[] _hashes;
    private readonly int _capacity;
    private int _confirmedFrame = -1;

    public RollbackSession(int capacityFrames = 600)
    {
        _capacity = capacityFrames;
        _inputs = new InputState[capacityFrames];
        _hashes = new ulong[capacityFrames];
    }

    public int ConfirmedFrame => _confirmedFrame;

    /// <summary>Record local/remote input for a frame and its post-tick state hash.</summary>
    public void Record(int frame, InputState remote, ulong stateHash)
    {
        _inputs[frame % _capacity] = remote;
        _hashes[frame % _capacity] = stateHash;
        if (frame > _confirmedFrame) _confirmedFrame = frame;
    }

    /// <summary>Hash comparison used to detect desync early; xxHash64 via FpHash.</summary>
    public bool HashMatches(int frame, ulong stateHash) => _hashes[frame % _capacity] == stateHash;
}
