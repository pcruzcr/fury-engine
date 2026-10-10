// Fighter.Core — fixed-capacity ring buffer (§17). Base capacity 8 (SimulationUnits.RingCapacity).
namespace Fighter.Core;

/// <summary>Deterministic ring buffer: array + head index. No allocations after construction.</summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _buf;
    private int _head; // index of oldest element
    /// <summary>Fixed capacity (set at construction, never grows).</summary>
    public int Capacity => _buf.Length;
    /// <summary>Stored elements (≤ Capacity). Oldest is dropped on overflow.</summary>
    public int Count { get; private set; }

    /// <summary>Stored frame count. Alias of <see cref="Count"/> under the
    /// session's hot-path contract name (FrameCount).</summary>
    public int FrameCount => Count;

    /// <summary>Creates a ring buffer with fixed <paramref name="capacity"/> (&gt; 0).</summary>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buf = new T[capacity];
    }

    /// <summary>Appends <paramref name="item"/>, overwriting the oldest on overflow.</summary>
    public void Push(T item)
    {
        if (Count < Capacity)
        {
            _buf[( _head + Count) % Capacity] = item;
            Count++;
        }
        else
        {
            _buf[_head] = item;
            _head = (_head + 1) % Capacity;
        }
    }

    /// <summary>offset 0 = latest, 1 = previous, ... Throws if out of stored range.</summary>
    /// <param name="offset">Frames ago (0 = latest).</param>
    /// <returns>The stored element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when out of stored range.</exception>
    public T GetLatest(int offset)
    {
        if ((uint)offset >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(offset));
        // Dividend (_head + Count - 1 - offset) is provably >= 0, so % never
        // yields a negative index: no correction branch (branch-coverage honest).
        int idx = (_head + Count - 1 - offset) % Capacity;
        return _buf[idx];
    }

    /// <summary>Non-throwing latest read: framesAgo 0 = latest.</summary>
    /// <param name="framesAgo">Frames ago (0 = latest).</param>
    /// <param name="value">Element when present; default otherwise.</param>
    /// <returns>True when an element exists at that age.</returns>
    public bool TryPeekAt(int framesAgo, out T value)
    {
        if (framesAgo < 0 || (uint)framesAgo >= (uint)Count)
        {
            value = default!;
            return false;
        }
        // Same provably-non-negative dividend as GetLatest: no correction branch.
        int idx = (_head + Count - 1 - framesAgo) % Capacity;
        value = _buf[idx];
        return true;
    }

    /// <summary>Copies oldest→newest into <paramref name="dst"/> (must fit Count). For snapshots.</summary>
    /// <param name="dst">Destination span; too-short destinations throw (no silent truncation).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="dst"/> is shorter than Count.</exception>
    public void CopyTo(System.Span<T> dst)
    {
        if (dst.Length < Count) throw new ArgumentException("Destination too small.", nameof(dst));
        for (int i = 0; i < Count; i++)
            dst[i] = PeekOldest(i);
    }

    /// <summary>Copies oldest→newest into <paramref name="dst"/> (must fit Count). For snapshots.</summary>
    public void CopyOldestFirst(T[] dst)
    {
        if (dst.Length < Count) throw new ArgumentException("Destination too small.", nameof(dst));
        for (int i = 0; i < Count; i++)
            dst[i] = PeekOldest(i);
    }

    /// <summary>Allocation-free oldest-first read (hot paths: snapshot encode, motion match).</summary>
    public T PeekOldest(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _buf[(_head + index) % Capacity];
    }

    /// <summary>Resets to empty (contents cleared).</summary>
    public void Clear() { Count = 0; _head = 0; Array.Clear(_buf, 0, _buf.Length); }
}
