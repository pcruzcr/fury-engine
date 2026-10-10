// Fighter.Core — replay primitives (§34, F0 foundation).
// Records: header (magic/version/seed/tick) + per-frame P1/P2 inputs.
// Verify: re-applies inputs to a fresh sim and compares per-frame hashes.
namespace Fighter.Core;

/// <summary>Substrate replay recorder (.DFRP): header + per-frame P1/P2 inputs.</summary>
public sealed class ReplayRecorder
{
    private readonly ulong _seed;
    private readonly System.Collections.Generic.List<(InputState P1, InputState P2)> _frames = new();

    /// <summary>Starts a recording bound to <paramref name="seed"/>.</summary>
    public ReplayRecorder(ulong seed) => _seed = seed;
    /// <summary>Appends one frame of inputs.</summary>
    public void Record(InputState p1, InputState p2) => _frames.Add((p1, p2));
    /// <summary>Recorded frame count.</summary>
    public int Count => _frames.Count;

    /// <summary>Serializes header + frames (little-endian).</summary>
    public byte[] ToBytes()
    {
        var w = new SnapshotWriter();
        w.Write(unchecked((int)0x44524650)); // "DFRP" LE
        w.Write(1); // format version
        w.Write(_seed);
        w.Write(_frames.Count);
        foreach (var (p1, p2) in _frames) { w.Write(p1); w.Write(p2); }
        return w.ToArray();
    }

    /// <summary>Parses and validates a recording (magic, version, bounds, exact length).</summary>
    public static ReplayPlayer Load(byte[] bytes) => new(bytes);
}

/// <summary>Validated substrate replay: seed + input streams, replayable frame by frame.</summary>
public sealed class ReplayPlayer
{
    private readonly ulong _seed;
    private readonly InputState[] _p1;
    private readonly InputState[] _p2;

    /// <summary>Recording seed (must match the live sim for reproduction).</summary>
    public ulong Seed => _seed;
    /// <summary>Frame count.</summary>
    public int Count => _p1.Length;

    internal ReplayPlayer(byte[] bytes)
    {
        try
        {
            var r = new SnapshotReader(bytes);
            if (r.ReadInt() != unchecked((int)0x44524650)) throw new ReplayCorruptException("Bad replay magic.");
            if (r.ReadInt() != 1) throw new ReplayCorruptException("Unsupported replay version.");
            _seed = r.ReadULong();
            int n = r.ReadInt();
            if (n < 0 || n > 60 * 60 * 60) throw new ReplayCorruptException("Implausible frame count.");
            _p1 = new InputState[n]; _p2 = new InputState[n];
            for (int i = 0; i < n; i++) { _p1[i] = r.ReadInput(); _p2[i] = r.ReadInput(); }
            if (!r.End) throw new ReplayCorruptException("Replay trailing bytes.");
        }
        catch (InvalidOperationException ex) when (ex is not ReplayCorruptException)
        {
            throw new ReplayCorruptException(ex.Message, ex);
        }
    }

    /// <summary>Replays all frames on a fresh sim. Returns per-frame hashes for comparison.</summary>
    public ulong[] PlayAll(int roundTimer = 60 * 99)
    {
        var sim = new DeterministicSim(_seed, roundTimer);
        var hashes = new ulong[_p1.Length];
        for (int i = 0; i < _p1.Length; i++)
        {
            sim.Step(_p1[i], _p2[i]);
            hashes[i] = sim.Capture().Hash;
        }
        return hashes;
    }
}
