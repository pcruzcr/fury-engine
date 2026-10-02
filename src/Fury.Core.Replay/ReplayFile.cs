using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;

namespace Fury.Core.Replay;

/// <summary>What a replay needs to reproduce its fight bit-exactly. Fingerprints reject replays recorded
/// against different rules, move data or snapshot layout instead of silently desyncing.</summary>
public sealed record ReplayHeader(
    ulong Seed,
    string P1Id,
    string P2Id,
    ulong ConfigHash,
    ulong P1Hash,
    ulong P2Hash,
    int SnapshotWords)
{
    public static readonly ReplayHeader Empty = new(0, "", "", 0, 0, 0, FightState.WordCount);

    /// <summary>Header describing a fight between two configured characters.</summary>
    public static ReplayHeader For(CharacterConfig p1, CharacterConfig p2, FightConfig cfg, ulong seed = 0, int snapshotWords = FightState.WordCount) =>
        new(seed, p1.Id, p2.Id, cfg.Fingerprint(), p1.Fingerprint(), p2.Fingerprint(), snapshotWords);

    /// <summary>Header for a live simulation (includes the snapshot size of its modules).</summary>
    public static ReplayHeader ForSimulation(FightSimulation sim, ulong seed = 0)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return For(sim.Data(0), sim.Data(1), sim.Config, seed, sim.SnapshotWords);
    }
}

/// <summary>Result of re-simulating a replay.</summary>
public readonly record struct ReplayVerification(bool Valid, int FailedFrame, string Reason)
{
    public static ReplayVerification Ok { get; } = new(true, -1, "");
}

/// <summary>
/// Deterministic replay: header + one input pair per frame + periodic state checkpoints + final hash,
/// sealed by a SHA-256 digest (integrity) or HMAC-SHA256 (authenticity, when the host supplies a key).
/// <para>
/// Two independent defences: the trailer detects corruption/tampering of the file, and
/// <see cref="Verify"/> re-simulates the inputs and compares the recorded checkpoints, so edited
/// inputs are caught even if an attacker recomputes the trailer. The binary layout is strictly
/// validated before any allocation: a hostile file can only be rejected, never crash the parser.
/// </para>
/// Layout (LE): "FRY4" | snapshotWords:2 | trailerMode:1 | seed:8 | configHash:8 | p1Hash:8 | p2Hash:8 |
/// p1IdLen:1 p1Id | p2IdLen:1 p2Id | frameCount:4 | checkpointInterval:2 | checkpointCount:4 | finalHash:8 |
/// inputs (u16 p1, u16 p2) × frameCount | checkpoints (u32 frame, u64 hash) × count | trailer:32.
/// </summary>
public sealed class ReplayFile
{
    public const uint Magic = 0x34595246; // "FRY4" little-endian
    public const int CheckpointInterval = 60;
    public const int MaxFrames = 60 * 60 * 60; // one hour
    public const int MaxIdBytes = 64;
    public const int TrailerBytes = 32;

    private const byte TrailerDigest = 0;
    private const byte TrailerHmac = 1;

    private readonly List<ushort> _p1 = [];
    private readonly List<ushort> _p2 = [];
    private readonly List<(int Frame, ulong Hash)> _checkpoints = [];

    public ReplayFile() : this(ReplayHeader.Empty)
    {
    }

    public ReplayFile(ReplayHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        Header = header;
    }

    public ReplayHeader Header { get; }

    public int FrameCount => _p1.Count;

    /// <summary>Hash of the state after the last recorded frame (0 until <see cref="Seal"/>).</summary>
    public ulong FinalHash { get; private set; }

    public IReadOnlyList<(int Frame, ulong Hash)> Checkpoints => _checkpoints;

    public void Append(InputState p1, InputState p2)
    {
        if (_p1.Count >= MaxFrames) throw new InvalidOperationException($"Replay exceeds {MaxFrames} frames.");
        _p1.Add(p1.Bits);
        _p2.Add(p2.Bits);
    }

    /// <summary>Records the state hash after <paramref name="frame"/> (recorders call this every <see cref="CheckpointInterval"/> frames).</summary>
    public void AddCheckpoint(int frame, ulong hash)
    {
        if (frame < 0 || frame >= FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        _checkpoints.Add((frame, hash));
    }

    /// <summary>Stores the final state hash. Call once when the fight ends.</summary>
    public void Seal(ulong finalHash) => FinalHash = finalHash;

    public (InputState P1, InputState P2) Get(int frame) => (new(_p1[frame]), new(_p2[frame]));

    /// <summary>Fingerprint over the input stream only (quick equality checks, not a security feature).</summary>
    public ulong ComputeDigest()
    {
        Span<byte> buffer = stackalloc byte[4];
        ulong hash = FpHash.HashBytes("FRY-IN"u8);
        for (int i = 0; i < FrameCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, _p1[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer[2..], _p2[i]);
            hash = FpHash.HashBytes(buffer, hash);
        }
        return FpHash.Combine(hash, (ulong)FrameCount);
    }

    // ------------------------------------------------------------------ verification

    /// <summary>
    /// Re-simulates the recorded inputs on a fresh simulation and compares every checkpoint and the final
    /// hash. <paramref name="factory"/> must build the fight described by <see cref="Header"/>.
    /// </summary>
    public ReplayVerification Verify(Func<FightSimulation> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var sim = factory();
        if (sim.SnapshotWords != Header.SnapshotWords)
        {
            return new(false, 0, $"Snapshot layout changed ({Header.SnapshotWords} -> {sim.SnapshotWords} words).");
        }

        int next = 0;
        for (int f = 0; f < FrameCount; f++)
        {
            sim.Step(new InputState(_p1[f]), new InputState(_p2[f]));
            while (next < _checkpoints.Count && _checkpoints[next].Frame == f)
            {
                if (sim.SnapshotHash != _checkpoints[next].Hash) return new(false, f, "Checkpoint hash mismatch.");
                next++;
            }
        }

        if (FrameCount > 0 && FinalHash != 0 && sim.SnapshotHash != FinalHash) return new(false, FrameCount - 1, "Final hash mismatch.");
        return ReplayVerification.Ok;
    }

    // ------------------------------------------------------------------ serialization

    /// <summary>Serializes the replay. With a <paramref name="macKey"/> the trailer is an HMAC-SHA256 (authentic); otherwise a SHA-256 digest.</summary>
    public byte[] ToBytes(ReadOnlySpan<byte> macKey = default)
    {
        byte[] p1Id = Encoding.UTF8.GetBytes(Header.P1Id);
        byte[] p2Id = Encoding.UTF8.GetBytes(Header.P2Id);
        if (p1Id.Length > MaxIdBytes || p2Id.Length > MaxIdBytes) throw new InvalidOperationException($"Character ids are limited to {MaxIdBytes} UTF-8 bytes.");

        int bodyLength = 4 + 2 + 1 + (8 * 4) + 1 + p1Id.Length + 1 + p2Id.Length + 4 + 2 + 4 + 8
            + (FrameCount * 4) + (_checkpoints.Count * 12);
        var data = new byte[bodyLength + TrailerBytes];
        var w = data.AsSpan();
        int o = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(w[o..], Magic); o += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(w[o..], (ushort)Header.SnapshotWords); o += 2;
        w[o++] = macKey.IsEmpty ? TrailerDigest : TrailerHmac;
        BinaryPrimitives.WriteUInt64LittleEndian(w[o..], Header.Seed); o += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(w[o..], Header.ConfigHash); o += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(w[o..], Header.P1Hash); o += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(w[o..], Header.P2Hash); o += 8;
        w[o++] = (byte)p1Id.Length; p1Id.CopyTo(w[o..]); o += p1Id.Length;
        w[o++] = (byte)p2Id.Length; p2Id.CopyTo(w[o..]); o += p2Id.Length;
        BinaryPrimitives.WriteInt32LittleEndian(w[o..], FrameCount); o += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(w[o..], CheckpointInterval); o += 2;
        BinaryPrimitives.WriteInt32LittleEndian(w[o..], _checkpoints.Count); o += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(w[o..], FinalHash); o += 8;
        for (int i = 0; i < FrameCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(w[o..], _p1[i]); o += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(w[o..], _p2[i]); o += 2;
        }
        foreach (var (frame, hash) in _checkpoints)
        {
            BinaryPrimitives.WriteInt32LittleEndian(w[o..], frame); o += 4;
            BinaryPrimitives.WriteUInt64LittleEndian(w[o..], hash); o += 8;
        }

        ComputeTrailer(w[..o], macKey, w[o..]);
        return data;
    }

    /// <summary>
    /// Parses a replay. Provide <paramref name="macKey"/> to require authenticity: an unauthenticated (digest)
    /// file is then rejected, so an attacker cannot downgrade the trailer.
    /// </summary>
    /// <exception cref="InvalidDataException">Truncated, oversized, tampered or inconsistent data.</exception>
    public static ReplayFile Parse(ReadOnlySpan<byte> data, ReadOnlySpan<byte> macKey = default)
    {
        const int fixedHead = 4 + 2 + 1 + (8 * 4) + 1;
        if (data.Length < fixedHead + 1 + 4 + 2 + 4 + 8 + TrailerBytes) throw new InvalidDataException("Replay truncated.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic) throw new InvalidDataException("Replay magic mismatch.");

        int body = data.Length - TrailerBytes;
        byte mode = data[6];
        if (mode is not (TrailerDigest or TrailerHmac)) throw new InvalidDataException("Unknown replay trailer mode.");
        if (!macKey.IsEmpty && mode != TrailerHmac) throw new InvalidDataException("Replay is not authenticated but authentication is required.");
        if (macKey.IsEmpty && mode == TrailerHmac) throw new InvalidDataException("Replay is authenticated: a key is required to open it.");

        Span<byte> expected = stackalloc byte[TrailerBytes];
        ComputeTrailer(data[..body], macKey, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, data[body..])) throw new InvalidDataException("Replay integrity check failed.");

        int o = 4;
        int snapshotWords = BinaryPrimitives.ReadUInt16LittleEndian(data[o..]); o += 2;
        o++; // mode
        ulong seed = BinaryPrimitives.ReadUInt64LittleEndian(data[o..]); o += 8;
        ulong configHash = BinaryPrimitives.ReadUInt64LittleEndian(data[o..]); o += 8;
        ulong p1Hash = BinaryPrimitives.ReadUInt64LittleEndian(data[o..]); o += 8;
        ulong p2Hash = BinaryPrimitives.ReadUInt64LittleEndian(data[o..]); o += 8;

        string p1Id = ReadId(data[..body], ref o);
        string p2Id = ReadId(data[..body], ref o);

        if (body - o < 4 + 2 + 4 + 8) throw new InvalidDataException("Replay truncated.");
        int frameCount = BinaryPrimitives.ReadInt32LittleEndian(data[o..]); o += 4;
        int interval = BinaryPrimitives.ReadUInt16LittleEndian(data[o..]); o += 2;
        int checkpointCount = BinaryPrimitives.ReadInt32LittleEndian(data[o..]); o += 4;
        ulong finalHash = BinaryPrimitives.ReadUInt64LittleEndian(data[o..]); o += 8;

        if (frameCount is < 0 or > MaxFrames) throw new InvalidDataException("Replay frame count out of range.");
        if (interval != CheckpointInterval) throw new InvalidDataException("Unsupported checkpoint interval.");
        if (checkpointCount < 0 || checkpointCount > frameCount) throw new InvalidDataException("Replay checkpoint count out of range.");
        long expectedBody = o + ((long)frameCount * 4) + ((long)checkpointCount * 12);
        if (expectedBody != body) throw new InvalidDataException("Replay length does not match its header.");

        var replay = new ReplayFile(new ReplayHeader(seed, p1Id, p2Id, configHash, p1Hash, p2Hash, snapshotWords));
        for (int i = 0; i < frameCount; i++)
        {
            replay._p1.Add(BinaryPrimitives.ReadUInt16LittleEndian(data[o..])); o += 2;
            replay._p2.Add(BinaryPrimitives.ReadUInt16LittleEndian(data[o..])); o += 2;
        }
        int previous = -1;
        for (int i = 0; i < checkpointCount; i++)
        {
            int frame = BinaryPrimitives.ReadInt32LittleEndian(data[o..]); o += 4;
            ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(data[o..]); o += 8;
            if (frame <= previous || frame >= frameCount) throw new InvalidDataException("Replay checkpoints must be strictly increasing and in range.");
            previous = frame;
            replay._checkpoints.Add((frame, hash));
        }
        replay.FinalHash = finalHash;
        return replay;
    }

    private static string ReadId(ReadOnlySpan<byte> body, ref int o)
    {
        if (o >= body.Length) throw new InvalidDataException("Replay truncated.");
        int len = body[o++];
        if (len > MaxIdBytes || o + len > body.Length) throw new InvalidDataException("Replay character id invalid.");
        string id;
        try
        {
            id = new UTF8Encoding(false, true).GetString(body.Slice(o, len));
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException("Replay character id is not valid UTF-8.", e);
        }
        o += len;
        return id;
    }

    private static void ComputeTrailer(ReadOnlySpan<byte> body, ReadOnlySpan<byte> macKey, Span<byte> dest)
    {
        if (macKey.IsEmpty) SHA256.HashData(body, dest);
        else HMACSHA256.HashData(macKey, body, dest);
    }
}

/// <summary>Records a live fight: steps the simulation and logs inputs + periodic checkpoints into a replay.</summary>
public sealed class ReplayRecorder
{
    private readonly FightSimulation _sim;

    public ReplayRecorder(FightSimulation sim, ReplayHeader header)
    {
        _sim = sim;
        Replay = new ReplayFile(header);
    }

    public ReplayFile Replay { get; }

    /// <summary>Steps the simulation one frame and records it.</summary>
    public void Step(InputState p1, InputState p2)
    {
        int frame = _sim.Frame;
        _sim.Step(p1, p2);
        Replay.Append(p1, p2);
        if ((frame + 1) % ReplayFile.CheckpointInterval == 0) Replay.AddCheckpoint(frame, _sim.SnapshotHash);
    }

    /// <summary>Finishes the recording and returns the sealed replay.</summary>
    public ReplayFile Finish()
    {
        Replay.Seal(_sim.SnapshotHash);
        return Replay;
    }
}
