using System;
using System.Collections.Generic;
using System.IO;
using Fury.Core.Data;

namespace Fury.Core.Authoring;

/// <summary>
/// Editable, engine-free model of one character's FMT1 move table. The frame data editor
/// edits this document and exports it with <see cref="BinaryMoveTableWriter"/>; the result
/// round-trips through <see cref="BinaryMoveTable.Parse"/>, the same validator the game uses.
/// </summary>
public sealed class MoveTableDocument
{
    private readonly List<MoveEntry> _moves;

    public MoveTableDocument(IEnumerable<MoveEntry> moves) => _moves = new List<MoveEntry>(moves);

    public static MoveTableDocument FromTable(BinaryMoveTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var moves = new List<MoveEntry>(table.MoveCount);
        for (int i = 0; i < table.MoveCount; i++) moves.Add(table.GetMove(i));
        return new MoveTableDocument(moves);
    }

    /// <exception cref="InvalidDataException">Blob is not a valid FMT1 table.</exception>
    public static MoveTableDocument FromBytes(ReadOnlySpan<byte> blob) => FromTable(BinaryMoveTable.Parse(blob));

    public int Count => _moves.Count;

    public MoveEntry Get(int index) => _moves[index];

    public void Set(int index, MoveEntry move)
    {
        Validate(move);
        _moves[index] = move;
    }

    /// <summary>Appends a move and returns its index.</summary>
    public int Add(MoveEntry move)
    {
        Validate(move);
        _moves.Add(move);
        return _moves.Count - 1;
    }

    /// <summary>
    /// Removes a move. The first <see cref="MoveSlot.Count"/> rows are the canonical slots
    /// the state machine indexes directly (StandLight … Super) and cannot be removed.
    /// </summary>
    public bool TryRemoveAt(int index)
    {
        if (index < MoveSlot.Count || index >= _moves.Count) return false;
        _moves.RemoveAt(index);
        return true;
    }

    public byte[] ToBytes() => BinaryMoveTableWriter.Write(_moves.ToArray());

    /// <exception cref="ArgumentOutOfRangeException">Frame counts or damage out of range.</exception>
    public static void Validate(MoveEntry m)
    {
        if (m.StartupFrames < 1) throw new ArgumentOutOfRangeException(nameof(m), "Startup must be >= 1 frame.");
        if (m.ActiveFrames < 1) throw new ArgumentOutOfRangeException(nameof(m), "Active must be >= 1 frame.");
        if (m.RecoveryFrames < 0) throw new ArgumentOutOfRangeException(nameof(m), "Recovery must be >= 0.");
        if (m.Damage < 0) throw new ArgumentOutOfRangeException(nameof(m), "Damage must be >= 0.");
        if (m.HitstopFrames < 0) throw new ArgumentOutOfRangeException(nameof(m), "Hitstop must be >= 0.");
        if (m.ReachRaw < 0 || m.BoxHalfHeightRaw < 0) throw new ArgumentOutOfRangeException(nameof(m), "Reach/box size must be >= 0.");

        // FMT2 fields.
        if (m.HitstunFrames < 0 || m.BlockstunFrames < 0) throw new ArgumentOutOfRangeException(nameof(m), "Hitstun/blockstun must be >= 0 (0 = legacy formula).");
        if (!Enum.IsDefined(m.Level)) throw new ArgumentOutOfRangeException(nameof(m), "Unknown hit level.");
        if (((int)m.Traits & ~AllTraits) != 0) throw new ArgumentOutOfRangeException(nameof(m), "Unknown move traits.");
        if (m.Category is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(m), "Category must be 0..7.");
        if ((m.CancelMask & ~0xFF) != 0) throw new ArgumentOutOfRangeException(nameof(m), "Cancel mask uses categories 0..7 only.");
        if (m.MeterCost is < 0 or > 300) throw new ArgumentOutOfRangeException(nameof(m), "Meter cost must be 0..300 EX points.");
        if (m.LaunchVyRaw < 0) throw new ArgumentOutOfRangeException(nameof(m), "Launch velocity must be >= 0 (positive = up).");
        if (m.Has(MoveTraits.Projectile))
        {
            if (m.ProjectileLifetime < 1) throw new ArgumentOutOfRangeException(nameof(m), "A projectile move needs a lifetime >= 1.");
            if (m.ProjectileSpawnFrame < 1 || m.ProjectileSpawnFrame > m.TotalFrames) throw new ArgumentOutOfRangeException(nameof(m), "The projectile must spawn within the move.");
            if (m.ProjectileSpeedRaw == 0) throw new ArgumentOutOfRangeException(nameof(m), "A projectile needs a speed.");
        }
        if (m.Has(MoveTraits.Air) && m.Has(MoveTraits.Throw)) throw new ArgumentOutOfRangeException(nameof(m), "Throws are ground-only.");
    }

    private static readonly int AllTraits = (int)(MoveTraits.Air | MoveTraits.Launcher | MoveTraits.Knockdown | MoveTraits.Throw | MoveTraits.Projectile | MoveTraits.Unblockable);
}
