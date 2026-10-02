using System.Buffers.Binary;

namespace Fury.Core.Data;

/// <summary>Where an attack must be blocked. Resolved against the defender's stance.</summary>
public enum HitLevel
{
    /// <summary>Blockable standing or crouching.</summary>
    Mid = 0,

    /// <summary>Blockable standing or crouching.</summary>
    High = 1,

    /// <summary>Must be blocked crouching.</summary>
    Low = 2,

    /// <summary>Must be blocked standing.</summary>
    Overhead = 3,
}

/// <summary>Bit flags describing special properties of a move.</summary>
[Flags]
public enum MoveTraits
{
    None = 0,

    /// <summary>Can be performed while airborne (jump attacks).</summary>
    Air = 1 << 0,

    /// <summary>Sends a grounded defender into the air (juggle starter).</summary>
    Launcher = 1 << 1,

    /// <summary>Hit knocks the defender down.</summary>
    Knockdown = 1 << 2,

    /// <summary>Grab: ignores blocking, requires a grounded defender, can be teched by a simultaneous grab.</summary>
    Throw = 1 << 3,

    /// <summary>Spawns a projectile at <see cref="MoveEntry.ProjectileSpawnFrame"/> instead of using a melee hitbox.</summary>
    Projectile = 1 << 4,

    /// <summary>Cannot be blocked.</summary>
    Unblockable = 1 << 5,
}

/// <summary>
/// One compiled move: a row of the binary frame-data table. All timings are sim frames. The first ten fields
/// are the original FMT1 layout (positional, so existing authoring code keeps compiling); everything added by
/// FMT2 is an init-only property whose zero default reproduces the pre-FMT2 behaviour, so a FMT1 table (or a
/// move that simply does not set them) plays exactly as before.
/// </summary>
public readonly record struct MoveEntry(
    int NameHash,
    int StartupFrames,
    int ActiveFrames,
    int RecoveryFrames,
    int Damage,
    int HitstopFrames,
    int PushbackRaw,
    int ReachRaw,
    int BoxHalfHeightRaw,
    int BoxCenterYRaw)
{
    /// <summary>Frames the defender stays in hitstun. 0 = legacy formula (recovery + 6).</summary>
    public int HitstunFrames { get; init; }

    /// <summary>Frames the defender stays in blockstun. 0 = legacy formula (recovery / 2 + 3).</summary>
    public int BlockstunFrames { get; init; }

    public MoveTraits Traits { get; init; }

    public HitLevel Level { get; init; }

    /// <summary>
    /// Packed trigger (see <c>Fury.Core.Sim.MoveCommand</c>): button mask, motion input and stance.
    /// 0 = cannot be triggered from input (follow-ups, projectiles owned by other moves).
    /// </summary>
    public int Command { get; init; }

    /// <summary>Category of this move (0 normal, 1 command normal, 2 special, 3 super, ...) used by cancel rules.</summary>
    public int Category { get; init; }

    /// <summary>Bit i set = this move may cancel into any move of category i once it has connected (hit or block).</summary>
    public int CancelMask { get; init; }

    /// <summary>Upward launch velocity (Q16.16, positive = up) applied to the defender when <see cref="MoveTraits.Launcher"/> hits.</summary>
    public int LaunchVyRaw { get; init; }

    /// <summary>EX meter points spent when the move starts.</summary>
    public int MeterCost { get; init; }

    /// <summary>Resolver tie-break when several commands match: higher wins, then the higher table index.</summary>
    public int Priority { get; init; }

    /// <summary>Horizontal speed of the projectile this move spawns (Q16.16 per frame, facing-relative).</summary>
    public int ProjectileSpeedRaw { get; init; }

    /// <summary>Lifetime in frames of the spawned projectile.</summary>
    public int ProjectileLifetime { get; init; }

    /// <summary>State frame at which the projectile appears.</summary>
    public int ProjectileSpawnFrame { get; init; }

    /// <summary>Hitstun applied on hit (data value, or the legacy derivation).</summary>
    public int EffectiveHitstun => HitstunFrames > 0 ? HitstunFrames : RecoveryFrames + 6;

    /// <summary>Blockstun applied on block (data value, or the legacy derivation).</summary>
    public int EffectiveBlockstun => BlockstunFrames > 0 ? BlockstunFrames : (RecoveryFrames / 2) + 3;

    public int TotalFrames => StartupFrames + ActiveFrames + RecoveryFrames;

    public bool Has(MoveTraits flag) => (Traits & flag) != 0;
}

/// <summary>
/// Parser for the binary frame-data tables (ADR-003). Little-Endian explicit and strictly validated: magic,
/// version and exact expected length; any deviation is a hard failure, never a silent read.
/// Two versions are readable: FMT1 (10 ints per move) and FMT2 (23 ints per move). FMT1 moves are upgraded
/// in memory with zero defaults, so both play identically. The writer always emits FMT2.
/// Layout: magic "FMT1"|"FMT2" | version:4 | moveCount:4 | per move N ints (all Q16.16 raws where dimensional).
/// </summary>
public sealed class BinaryMoveTable
{
    public const int Version = 2;
    public const int LegacyVersion = 1;
    public const int HeaderBytes = 12;
    public const int IntsPerMove = 23;
    public const int BytesPerMove = IntsPerMove * 4;
    public const int LegacyIntsPerMove = 10;
    public const int MaxMoves = 4096;

    private readonly int[] _words;
    private readonly int _moveCount;

    private BinaryMoveTable(int[] words, int moveCount)
    {
        _words = words;
        _moveCount = moveCount;
    }

    public int MoveCount => _moveCount;

    /// <summary>Order-dependent content fingerprint of the whole table (replay/netplay compatibility checks).</summary>
    public ulong ContentHash => Fury.Core.Math.FpHash.HashInts(_words);

    /// <summary>Parses and validates a raw blob. Throws <see cref="InvalidDataException"/> on any structural fault.</summary>
    public static BinaryMoveTable Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < HeaderBytes) throw new InvalidDataException("Move table truncated: missing header.");
        bool v1 = blob[0] == (byte)'F' && blob[1] == (byte)'M' && blob[2] == (byte)'T' && blob[3] == (byte)'1';
        bool v2 = blob[0] == (byte)'F' && blob[1] == (byte)'M' && blob[2] == (byte)'T' && blob[3] == (byte)'2';
        if (!v1 && !v2) throw new InvalidDataException("Move table magic mismatch.");

        int version = BinaryPrimitives.ReadInt32LittleEndian(blob[4..]);
        if (version != (v1 ? LegacyVersion : Version)) throw new InvalidDataException($"Move table version {version} unsupported for this magic.");

        int moveCount = BinaryPrimitives.ReadInt32LittleEndian(blob[8..]);
        if (moveCount < 0 || moveCount > MaxMoves) throw new InvalidDataException("Move table move count out of range.");

        int srcInts = v1 ? LegacyIntsPerMove : IntsPerMove;
        long expected = HeaderBytes + ((long)moveCount * srcInts * 4);
        if (blob.Length != expected) throw new InvalidDataException($"Move table length {blob.Length} != expected {expected}.");

        var words = new int[moveCount * IntsPerMove];
        for (int m = 0; m < moveCount; m++)
        {
            for (int i = 0; i < srcInts; i++)
            {
                words[(m * IntsPerMove) + i] = BinaryPrimitives.ReadInt32LittleEndian(blob[(HeaderBytes + (((m * srcInts) + i) * 4))..]);
            }
        }
        return new BinaryMoveTable(words, moveCount);
    }

    public MoveEntry GetMove(int index)
    {
        if ((uint)index >= (uint)_moveCount) throw new ArgumentOutOfRangeException(nameof(index));
        ReadOnlySpan<int> w = _words.AsSpan(index * IntsPerMove, IntsPerMove);
        return new MoveEntry(w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7], w[8], w[9])
        {
            HitstunFrames = w[10],
            BlockstunFrames = w[11],
            Traits = (MoveTraits)w[12],
            Level = (HitLevel)w[13],
            Command = w[14],
            Category = w[15],
            CancelMask = w[16],
            LaunchVyRaw = w[17],
            MeterCost = w[18],
            Priority = w[19],
            ProjectileSpeedRaw = w[20],
            ProjectileLifetime = w[21],
            ProjectileSpawnFrame = w[22],
        };
    }

    /// <summary>Copies one move into <paramref name="dest"/> in table order (the inverse of <see cref="GetMove"/>).</summary>
    internal static void Flatten(in MoveEntry m, Span<int> dest)
    {
        dest[0] = m.NameHash;
        dest[1] = m.StartupFrames;
        dest[2] = m.ActiveFrames;
        dest[3] = m.RecoveryFrames;
        dest[4] = m.Damage;
        dest[5] = m.HitstopFrames;
        dest[6] = m.PushbackRaw;
        dest[7] = m.ReachRaw;
        dest[8] = m.BoxHalfHeightRaw;
        dest[9] = m.BoxCenterYRaw;
        dest[10] = m.HitstunFrames;
        dest[11] = m.BlockstunFrames;
        dest[12] = (int)m.Traits;
        dest[13] = (int)m.Level;
        dest[14] = m.Command;
        dest[15] = m.Category;
        dest[16] = m.CancelMask;
        dest[17] = m.LaunchVyRaw;
        dest[18] = m.MeterCost;
        dest[19] = m.Priority;
        dest[20] = m.ProjectileSpeedRaw;
        dest[21] = m.ProjectileLifetime;
        dest[22] = m.ProjectileSpawnFrame;
    }
}

/// <summary>Writer for the FMT2 format: used by the authoring pipeline to emit tables.</summary>
public static class BinaryMoveTableWriter
{
    public static byte[] Write(ReadOnlySpan<MoveEntry> moves)
    {
        if (moves.Length > BinaryMoveTable.MaxMoves) throw new ArgumentException($"At most {BinaryMoveTable.MaxMoves} moves per table.", nameof(moves));

        var blob = new byte[BinaryMoveTable.HeaderBytes + (moves.Length * BinaryMoveTable.BytesPerMove)];
        blob[0] = (byte)'F';
        blob[1] = (byte)'M';
        blob[2] = (byte)'T';
        blob[3] = (byte)'2';
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4), BinaryMoveTable.Version);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(8), moves.Length);

        Span<int> row = stackalloc int[BinaryMoveTable.IntsPerMove];
        for (int i = 0; i < moves.Length; i++)
        {
            BinaryMoveTable.Flatten(moves[i], row);
            int offset = BinaryMoveTable.HeaderBytes + (i * BinaryMoveTable.BytesPerMove);
            for (int w = 0; w < row.Length; w++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(offset + (w * 4)), row[w]);
            }
        }
        return blob;
    }

    /// <summary>Writes the legacy FMT1 layout (10 fields only). Kept for tooling that still targets it and for tests.</summary>
    public static byte[] WriteLegacy(ReadOnlySpan<MoveEntry> moves)
    {
        var blob = new byte[BinaryMoveTable.HeaderBytes + (moves.Length * BinaryMoveTable.LegacyIntsPerMove * 4)];
        blob[0] = (byte)'F';
        blob[1] = (byte)'M';
        blob[2] = (byte)'T';
        blob[3] = (byte)'1';
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4), BinaryMoveTable.LegacyVersion);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(8), moves.Length);

        Span<int> row = stackalloc int[BinaryMoveTable.IntsPerMove];
        for (int i = 0; i < moves.Length; i++)
        {
            BinaryMoveTable.Flatten(moves[i], row);
            for (int w = 0; w < BinaryMoveTable.LegacyIntsPerMove; w++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BinaryMoveTable.HeaderBytes + (((i * BinaryMoveTable.LegacyIntsPerMove) + w) * 4)), row[w]);
            }
        }
        return blob;
    }
}
