namespace Fury.Core.Rollback;

/// <summary>
/// Fixed-capacity ring of full state snapshots, one slot per frame, preallocated at fight
/// start (zero allocation per save/restore). Save/restore is the rollback primitive:
/// Restore(f) + replay of stored inputs resimulates history deterministically (ADR-004).
/// </summary>
public sealed class StateStore
{
    private readonly int[][] _frames;
    private readonly int[] _frameNumbers;
    private readonly int _wordCount;
    private readonly int _capacity;

    public StateStore(int wordCount, int capacityFrames = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wordCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityFrames);
        _wordCount = wordCount;
        _capacity = capacityFrames;
        _frames = new int[capacityFrames][];
        _frameNumbers = new int[capacityFrames];
        for (int i = 0; i < capacityFrames; i++)
        {
            _frames[i] = new int[wordCount];
            _frameNumbers[i] = -1;
        }
    }

    /// <summary>Saves the snapshot for a frame (overwrites the oldest slot on wrap).</summary>
    public void Save(int frame, ReadOnlySpan<int> snapshot)
    {
        if (snapshot.Length < _wordCount) throw new ArgumentException("Snapshot smaller than word count.");
        int slot = frame % _capacity;
        snapshot[.._wordCount].CopyTo(_frames[slot]);
        _frameNumbers[slot] = frame;
    }

    /// <summary>Loads the snapshot for a frame; false when the slot was overwritten (too old).</summary>
    public bool TryLoad(int frame, Span<int> dest)
    {
        if (dest.Length < _wordCount) throw new ArgumentException("Destination smaller than word count.");
        int slot = frame % _capacity;
        if (_frameNumbers[slot] != frame) return false;
        _frames[slot].AsSpan(0, _wordCount).CopyTo(dest);
        return true;
    }

    public int Capacity => _capacity;
}
