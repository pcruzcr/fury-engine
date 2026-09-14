using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Fury.Core.Memory;

/// <summary>
/// Bump allocator over one contiguous block reserved at fight start (frame 0).
/// Zero GC pressure during the fight: transient scratch calls <see cref="Reset"/> each frame;
/// live state is allocated once. Slices reference the backing buffer directly — no pinning,
/// GC-safe and AOT-safe. All fight memory flows through this allocator (ADR-002).
/// </summary>
public sealed class Arena
{
    private const int Alignment = 8;
    private readonly byte[] _buffer;
    private int _offset;

    public Arena(int capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        _buffer = new byte[capacityBytes];
        CapacityBytes = capacityBytes;
    }

    public int CapacityBytes { get; }

    /// <summary>Bytes consumed since construction or the last reset (for observability budgets).</summary>
    public int UsedBytes => _offset;

    /// <summary>
    /// Allocate space for <paramref name="count"/> unmanaged <typeparamref name="T"/> items.
    /// Throws when the arena is exhausted — the caller sized the arena wrong at frame 0.
    /// </summary>
    public ArenaSlice<T> Alloc<T>(int count) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        int bytes = checked(count * Unsafe.SizeOf<T>());
        int start = (_offset + (Alignment - 1)) & ~(Alignment - 1);
        if (bytes > CapacityBytes - start) throw new InvalidOperationException($"Arena exhausted: requested {bytes} B, {CapacityBytes - start} B free.");
        _offset = start + bytes;
        return new ArenaSlice<T>(_buffer, start, count);
    }

    /// <summary>Reclaim the whole arena (per-frame scratch). Live-state arenas never call this mid-fight.</summary>
    public void Reset() => _offset = 0;
}

/// <summary>
/// Typed view over an arena region. Not a root to the buffer's contents — obtain
/// <see cref="Span"/> at use sites; holding the slice across frames is legal (the backing
/// array never moves allocations), holding the <see cref="Span"/> is not.
/// </summary>
public readonly struct ArenaSlice<T> where T : unmanaged
{
    private readonly byte[] _buffer;
    private readonly int _offset;
    private readonly int _length;

    internal ArenaSlice(byte[] buffer, int offset, int length)
    {
        _buffer = buffer;
        _offset = offset;
        _length = length;
    }

    public int Length => _length;

    public Span<T> Span => MemoryMarshal.Cast<byte, T>(_buffer.AsSpan(_offset, _length * Unsafe.SizeOf<T>()));
}
