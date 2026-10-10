// Fighter.Core — fight replay file (.frep): deterministic binary format for full matches.
// Layout (all little-endian, mirroring SnapshotWriter conventions):
//   magic "DFFR" (int) | format version (int, 1) | seed (ulong) | frame count (int)
//   per frame: P1 InputState (4 ints) + P2 InputState (4 ints)
// File IO is deterministic (no clocks, no randomness); tools/HUD consume it. Replay truth
// stays input-based: given seed + inputs, any build reproduces every frame hash (§34).
namespace Fighter.Core;

/// <summary>Fight replay file (.frep v1): seed + per-frame input streams.</summary>
public static class FightReplayFile
{
    /// <summary>Format version (bumped on layout change; loaders reject mismatch loudly).</summary>
    public const int FormatVersion = 1;
    private const int Magic = unchecked((int)0x44464652); // "DFFR" LE

    /// <summary>Writes seed + frames to <paramref name="path"/> (little-endian).</summary>
    public static void Save(string path, ulong seed,
        System.Collections.Generic.IReadOnlyList<(InputState P1, InputState P2)> frames)
    {
        var w = new SnapshotWriter();
        w.Write(Magic);
        w.Write(FormatVersion);
        w.Write(seed);
        w.Write(frames.Count);
        foreach (var (p1, p2) in frames) { w.Write(p1); w.Write(p2); }
        System.IO.File.WriteAllBytes(path, w.ToArray());
    }

    /// <summary>Loads and validates a file (magic, version, bounds, exact length).</summary>
    public static (ulong Seed, InputState[] P1, InputState[] P2) Load(string path) =>
        LoadBytes(System.IO.File.ReadAllBytes(path));

    /// <summary>Parses validated bytes (same checks as <see cref="Load"/>).</summary>
    public static (ulong Seed, InputState[] P1, InputState[] P2) LoadBytes(byte[] bytes)
    {
        try
        {
            var r = new SnapshotReader(bytes);
            if (r.ReadInt() != Magic) throw new ReplayCorruptException("Bad .frep magic.");
            if (r.ReadInt() != FormatVersion) throw new ReplayCorruptException("Unsupported .frep version.");
            ulong seed = r.ReadULong();
            int n = r.ReadInt();
            if (n < 0 || n > 60 * 60 * 120) throw new ReplayCorruptException("Implausible .frep frame count.");
            var p1 = new InputState[n];
            var p2 = new InputState[n];
            for (int i = 0; i < n; i++) { p1[i] = r.ReadInput(); p2[i] = r.ReadInput(); }
            if (!r.End) throw new ReplayCorruptException(".frep trailing bytes.");
            return (seed, p1, p2);
        }
        catch (InvalidOperationException ex) when (ex is not ReplayCorruptException)
        {
            throw new ReplayCorruptException(ex.Message, ex);
        }
    }
}
