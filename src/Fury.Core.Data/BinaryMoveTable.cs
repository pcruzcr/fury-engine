using System.Buffers.Binary;

namespace Fury.Core.Data;

/// <summary>
/// One compiled move: a row of the binary frame-data table. All timings are sim frames.
/// </summary>
public readonly record struct MoveEntry(
    int NameHash,
    int StartupFrames,
    int ActiveFrames,
    int RecoveryFrames,
    int Damage,
    int HitstopFrames,
    int PushbackRaw);

/// <summary>
/// Parser for the FMT1 binary frame-data table (ADR-003): Little-Endian explicit, rigorous
/// structural validation — magic, version, exact expected length. Any deviation is a hard
/// failure, never a silent read. The FSM reads transitions from these tables in hot path;
/// changing frame data requires regenerating the binary, not recompiling C#.
/// Layout: magic "FMT1" | version:4 | moveCount:4 | per move: 7 ints (28 B).
/// </summary>
public sealed class BinaryMoveTable
{
    public const int Version = 1;
    public const int HeaderBytes = 12;
    public const int BytesPerMove = 28;
    public const int IntsPerMove = 7;

    private readonly int[] _words;
    private readonly int _moveCount;

    private BinaryMoveTable(int[] words, int moveCount)
    {
        _words = words;
        _moveCount = moveCount;
    }

    public int MoveCount => _moveCount;

    /// <summary>Parses and validates a raw blob. Throws <see cref="InvalidDataException"/> on any structural fault.</summary>
    public static BinaryMoveTable Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < HeaderBytes) throw new InvalidDataException("FMT1 blob truncated: missing header.");
        if (blob[0] != (byte)'F' || blob[1] != (byte)'M' || blob[2] != (byte)'T' || blob[3] != (byte)'1')
        {
            throw new InvalidDataException("FMT1 magic mismatch.");
        }
        int version = BinaryPrimitives.ReadInt32LittleEndian(blob[4..]);
        if (version != Version) throw new InvalidDataException($"FMT1 version {version} unsupported.");
        int moveCount = BinaryPrimitives.ReadInt32LittleEndian(blob[8..]);
        if (moveCount < 0) throw new InvalidDataException("FMT1 negative move count.");
        long expected = HeaderBytes + (long)moveCount * BytesPerMove;
        if (blob.Length != expected) throw new InvalidDataException($"FMT1 length {blob.Length} != expected {expected}.");

        var words = new int[moveCount * IntsPerMove];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadInt32LittleEndian(blob[(HeaderBytes + i * 4)..]);
        }
        return new BinaryMoveTable(words, moveCount);
    }

    public MoveEntry GetMove(int index)
    {
        if ((uint)index >= (uint)_moveCount) throw new ArgumentOutOfRangeException(nameof(index));
        int b = index * IntsPerMove;
        return new MoveEntry(_words[b], _words[b + 1], _words[b + 2], _words[b + 3], _words[b + 4], _words[b + 5], _words[b + 6]);
    }

    /// <summary>Linear scan by xxHash-like int name hash (authoring pipeline stores the hash, not the string).</summary>
    public bool TryGetByNameHash(int nameHash, out MoveEntry entry)
    {
        for (int i = 0; i < _moveCount; i++)
        {
            var move = GetMove(i);
            if (move.NameHash == nameHash)
            {
                entry = move;
                return true;
            }
        }
        entry = default;
        return false;
    }
}

/// <summary>Writer for the FMT1 format — used by the authoring pipeline to emit tables.</summary>
public static class BinaryMoveTableWriter
{
    public static byte[] Write(ReadOnlySpan<MoveEntry> moves)
    {
        var blob = new byte[BinaryMoveTable.HeaderBytes + moves.Length * BinaryMoveTable.BytesPerMove];
        blob[0] = (byte)'F';
        blob[1] = (byte)'M';
        blob[2] = (byte)'T';
        blob[3] = (byte)'1';
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4), BinaryMoveTable.Version);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(8), moves.Length);
        for (int i = 0; i < moves.Length; i++)
        {
            int o = BinaryMoveTable.HeaderBytes + i * BinaryMoveTable.BytesPerMove;
            var m = moves[i];
            var target = blob.AsSpan(o);
            BinaryPrimitives.WriteInt32LittleEndian(target, m.NameHash);
            BinaryPrimitives.WriteInt32LittleEndian(target[4..], m.StartupFrames);
            BinaryPrimitives.WriteInt32LittleEndian(target[8..], m.ActiveFrames);
            BinaryPrimitives.WriteInt32LittleEndian(target[12..], m.RecoveryFrames);
            BinaryPrimitives.WriteInt32LittleEndian(target[16..], m.Damage);
            BinaryPrimitives.WriteInt32LittleEndian(target[20..], m.HitstopFrames);
            BinaryPrimitives.WriteInt32LittleEndian(target[24..], m.PushbackRaw);
        }
        return blob;
    }
}
