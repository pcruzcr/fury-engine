// Fighter.Core — fight replay format v2 (.fr2p).
// Layout (all little-endian):
//   HEADER: magic 3B (DF 00 01) | format u16 (2) | game version 16B (UTF-8,
//     null-padded) | rng state 4x u64 | stage id u8 | p1 char u8 | p2 char u8 |
//     platform u8 | compression u8 (0 = none; nonzero rejected loudly) |
//     frame count u32
//   BODY: initial FightSnapshot (u32 length + full payload, NOT delta) +
//     InputState frames packed 8B each (buttons u8, dirX i8, dirY i8,
//     reserved u8, frame i32)
//   FOOTER: xxHash64 of header+body (u64, see DeterminismHash).
// Compression: stored UNCOMPRESSED (flag 0). LZ4 adoption is OPEN pending a
// no-network package restore + IL2CPP vetting (see ADR-006); the flag byte
// reserves the upgrade path without a format break.
// RNG state: word0 = DeterministicRng state; words1-3 = 0 reserved for the
// xoshiro256** cutover (ADR-005), which will fill all four words.
namespace Fighter.Core;

/// <summary>Replay v2 platform id (host that recorded the replay).</summary>
public enum ReplayPlatform : byte
{
    /// <summary>Unknown / editor host (no OS match).</summary>
    Unknown = 0,
    /// <summary>Windows x64.</summary>
    Win64 = 1,
    /// <summary>Linux x64.</summary>
    Linux64 = 2,
    /// <summary>Android ARM64.</summary>
    AndroidArm64 = 3,
    /// <summary>Android x86_64 (emulator / Chromebook).</summary>
    AndroidX86 = 4,
    /// <summary>iOS ARM64.</summary>
    IOSArm64 = 5,
    /// <summary>macOS universal (ARM64 + x64).</summary>
    MacOSUniversal = 6,
}

/// <summary>Replay v2 character registry (u8 ids; 0 = unknown).</summary>
public static class ReplayCast
{
    /// <summary>Glen character id.</summary>
    public const byte Glen = 1;
    /// <summary>Kira character id.</summary>
    public const byte Kira = 2;

    /// <summary>Builds character data for <paramref name="id"/>.</summary>
    /// <param name="id">Character id.</param>
    /// <returns>Character data.</returns>
    /// <exception cref="ReplayCorruptException">Thrown on unknown id.</exception>
    public static FighterData DataFor(byte id) => id switch
    {
        Glen => GlenData.Build(),
        Kira => KiraData.Build(),
        _ => throw new ReplayCorruptException($"Unknown replay char id: {id}."),
    };

    /// <summary>Builds stage data for <paramref name="id"/> (matches <see cref="StageData"/> ids).</summary>
    /// <param name="id">Stage id.</param>
    /// <returns>Stage data.</returns>
    /// <exception cref="ReplayCorruptException">Thrown on unknown id.</exception>
    public static StageData StageFor(byte id) => id switch
    {
        StageData.ArenaNeutral => StageData.Arena(),
        StageData.DojoCentral => StageData.Dojo(),
        _ => throw new ReplayCorruptException($"Unknown replay stage id: {id}."),
    };
}

/// <summary>Validated replay v2: seed, cast, stage, initial snapshot, input streams.</summary>
public sealed class ReplayV2
{
    /// <summary>Format version (loaders reject mismatch loudly).</summary>
    public const ushort FormatVersion = 2;
    /// <summary>Game version stamped in the header (matches project.godot).</summary>
    public const string GameVersion = "0.2.0";
    /// <summary>Packed frame size in bytes.</summary>
    public const int PackedFrameBytes = 8;
    /// <summary>Maximum accepted frame count (2 hours at 60 Hz).</summary>
    public const int MaxFrames = 60 * 60 * 120;

    /// <summary>Player 1 character id.</summary>
    public byte P1Char => _p1Char;
    private readonly byte _p1Char;
    /// <summary>Player 2 character id.</summary>
    public byte P2Char => _p2Char;
    private readonly byte _p2Char;
    /// <summary>Stage id.</summary>
    public byte StageId => _stageId;
    private readonly byte _stageId;
    /// <summary>Recording host platform.</summary>
    public ReplayPlatform Platform => _platform;
    private readonly ReplayPlatform _platform;
    /// <summary>Recording seed (rng word0).</summary>
    public ulong Seed => _seed;
    private readonly ulong _seed;
    /// <summary>Initial snapshot (full, frame 0 state).</summary>
    public FightSnapshot Initial => _initial;
    private readonly FightSnapshot _initial;
    /// <summary>Player 1 inputs.</summary>
    public InputState[] P1 => _p1;
    private readonly InputState[] _p1;
    /// <summary>Player 2 inputs.</summary>
    public InputState[] P2 => _p2;
    private readonly InputState[] _p2;

    private ReplayV2(ulong seed, byte stageId, byte p1Char, byte p2Char,
        ReplayPlatform platform, FightSnapshot initial, InputState[] p1, InputState[] p2)
    {
        _seed = seed; _stageId = stageId; _p1Char = p1Char; _p2Char = p2Char;
        _platform = platform; _initial = initial; _p1 = p1; _p2 = p2;
    }

    /// <summary>Frame count.</summary>
    public int Count => _p1.Length;

    /// <summary>Detects the current host platform (never part of hashes).</summary>
    /// <returns>Best-effort platform id (Unknown when unmatched).</returns>
    public static ReplayPlatform DetectHost()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        var os = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform;
        if (os(System.Runtime.InteropServices.OSPlatform.Windows))
            return arch == System.Runtime.InteropServices.Architecture.X64 ? ReplayPlatform.Win64 : ReplayPlatform.Unknown;
        if (os(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            if (arch == System.Runtime.InteropServices.Architecture.X64) return ReplayPlatform.Linux64;
            if (arch == System.Runtime.InteropServices.Architecture.Arm64) return ReplayPlatform.AndroidArm64;
            if (arch == System.Runtime.InteropServices.Architecture.X64) return ReplayPlatform.AndroidX86;
            return ReplayPlatform.Unknown;
        }
        if (os(System.Runtime.InteropServices.OSPlatform.OSX)) return ReplayPlatform.MacOSUniversal;
        return ReplayPlatform.Unknown;
    }

    /// <summary>Packs one input into 8 bytes (buttons/dir/reserved/frame).</summary>
    /// <param name="s">Input to pack.</param>
    /// <param name="dst">Destination (advancing writer).</param>
    internal static void PackFrame(InputState s, SnapshotWriter dst)
    {
        dst.WriteByte((byte)s.Buttons);
        dst.WriteByte((byte)(sbyte)s.DirX);
        dst.WriteByte((byte)(sbyte)s.DirY);
        dst.WriteByte(0); // reserved, must be zero
        dst.Write(s.Frame);
    }

    /// <summary>Unpacks one 8-byte frame.</summary>
    /// <param name="src">Source (advancing reader).</param>
    /// <returns>Unpacked input.</returns>
    /// <exception cref="ReplayCorruptException">Thrown on reserved-byte or range violations.</exception>
    internal static InputState UnpackFrame(SnapshotReader src)
    {
        byte buttons = src.ReadByte();
        sbyte dx;
        sbyte dy;
        unchecked
        {
            dx = (sbyte)src.ReadByte();
            dy = (sbyte)src.ReadByte();
        }
        if (src.ReadByte() != 0) throw new ReplayCorruptException("Replay frame reserved byte nonzero.");
        if ((buttons & ~(byte)(Buttons.LP | Buttons.MP | Buttons.HP | Buttons.LK | Buttons.MK | Buttons.HK)) != 0)
            throw new ReplayCorruptException($"Replay frame unknown buttons: 0x{buttons:X2}.");
        int frame = src.ReadInt();
        try
        {
            return new InputState((Buttons)buttons, dx, dy, frame);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ReplayCorruptException("Replay frame axis out of range.", ex);
        }
    }

    /// <summary>Serializes a recording (header + body + xxHash64 footer).</summary>
    /// <param name="seed">Recording seed.</param>
    /// <param name="stageId">Stage id.</param>
    /// <param name="p1Char">Player 1 character id.</param>
    /// <param name="p2Char">Player 2 character id.</param>
    /// <param name="initial">Initial snapshot (full).</param>
    /// <param name="p1">Player 1 inputs.</param>
    /// <param name="p2">Player 2 inputs.</param>
    /// <param name="platform">Recording host (defaults to detected host).</param>
    /// <returns>Complete .fr2p bytes.</returns>
    /// <exception cref="ArgumentException">Thrown on stream length mismatch or oversize.</exception>
    public static byte[] SaveBytes(ulong seed, byte stageId, byte p1Char, byte p2Char,
        FightSnapshot initial, InputState[] p1, InputState[] p2,
        ReplayPlatform? platform = null)
    {
        if (p1 is null) throw new ArgumentNullException(nameof(p1));
        if (p2 is null) throw new ArgumentNullException(nameof(p2));
        if (p1.Length != p2.Length) throw new ArgumentException("Stream length mismatch.");
        if (p1.Length > MaxFrames) throw new ArgumentException($"Too many frames: {p1.Length}.");
        if (initial is null) throw new ArgumentNullException(nameof(initial));

        var w = new SnapshotWriter();
        w.WriteByte(0xDF); w.WriteByte(0x00); w.WriteByte(0x01); // magic bytes
        w.WriteUShort(FormatVersion);
        byte[] ver = System.Text.Encoding.UTF8.GetBytes(GameVersion);
        if (ver.Length > 16) throw new ArgumentException("Game version too long.");
        for (int i = 0; i < 16; i++) w.WriteByte(i < ver.Length ? ver[i] : (byte)0);
        w.Write(seed); w.Write(0UL); w.Write(0UL); w.Write(0UL); // rng words (1-3 reserved)
        w.WriteByte(stageId); w.WriteByte(p1Char); w.WriteByte(p2Char);
        w.WriteByte((byte)(platform ?? DetectHost()));
        w.WriteByte(0); // compression: 0 = none
        w.Write(p1.Length);
        w.Write(initial.Payload.Length);
        foreach (byte b in initial.Payload) w.WriteByte(b);
        for (int i = 0; i < p1.Length; i++) { PackFrame(p1[i], w); PackFrame(p2[i], w); }
        byte[] body = w.ToArray();
        ulong footer = DeterminismHash.Hash(body);
        var final = new SnapshotWriter(body.Length + 8);
        foreach (byte b in body) final.WriteByte(b);
        final.Write(footer);
        return final.ToArray();
    }

    /// <summary>Writes a recording to <paramref name="path"/>.</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="seed">Recording seed.</param>
    /// <param name="stageId">Stage id.</param>
    /// <param name="p1Char">Player 1 character id.</param>
    /// <param name="p2Char">Player 2 character id.</param>
    /// <param name="initial">Initial snapshot (full).</param>
    /// <param name="p1">Player 1 inputs.</param>
    /// <param name="p2">Player 2 inputs.</param>
    public static void Save(string path, ulong seed, byte stageId, byte p1Char, byte p2Char,
        FightSnapshot initial, InputState[] p1, InputState[] p2) =>
        System.IO.File.WriteAllBytes(path, SaveBytes(seed, stageId, p1Char, p2Char, initial, p1, p2));

    /// <summary>Loads and validates a file (magic, version, ids, lengths, footer hash).</summary>
    /// <param name="path">Source file.</param>
    /// <returns>Validated replay.</returns>
    /// <exception cref="ReplayCorruptException">Thrown on any integrity failure.</exception>
    public static ReplayV2 Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            throw new ReplayCorruptException($"Cannot read replay file: {path}.", ex);
        }
        return LoadBytes(bytes);
    }

    /// <summary>Parses validated bytes (same checks as <see cref="Load"/>).</summary>
    /// <param name="bytes">Complete .fr2p bytes.</param>
    /// <returns>Validated replay.</returns>
    /// <exception cref="ReplayCorruptException">Thrown on any integrity failure.</exception>
    public static ReplayV2 LoadBytes(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        try
        {
            if (bytes.Length < 8)
                throw new ReplayCorruptException("Replay too short for footer.");
            int bodyLen = bytes.Length - 8;
            ulong expected = DeterminismHash.Hash(new System.ReadOnlySpan<byte>(bytes, 0, bodyLen));
            ulong footer = ReadFooter(bytes, bodyLen);
            if (footer != expected)
                throw new ReplayCorruptException(
                    $"Replay footer mismatch: file=0x{footer:X16} computed=0x{expected:X16}.");

            var body = new byte[bodyLen];
            System.Array.Copy(bytes, body, bodyLen);
            var r = new SnapshotReader(body);
            if (r.ReadByte() != 0xDF || r.ReadByte() != 0x00 || r.ReadByte() != 0x01)
                throw new ReplayCorruptException("Bad replay v2 magic.");
            if (r.ReadUShort() != FormatVersion)
                throw new ReplayCorruptException("Unsupported replay v2 version.");
            byte[] verBytes = new byte[16];
            for (int i = 0; i < 16; i++) verBytes[i] = r.ReadByte();
            string ver = System.Text.Encoding.UTF8.GetString(verBytes).TrimEnd('\0');
            if (ver.Length == 0) throw new ReplayCorruptException("Empty replay game version.");
            ulong seed = r.ReadULong();
            _ = r.ReadULong(); _ = r.ReadULong(); _ = r.ReadULong(); // reserved rng words
            byte stageId = r.ReadByte();
            byte p1c = r.ReadByte();
            byte p2c = r.ReadByte();
            byte plat = r.ReadByte();
            if (!System.Enum.IsDefined(typeof(ReplayPlatform), plat))
                throw new ReplayCorruptException($"Unknown replay platform id: {plat}.");
            if (r.ReadByte() != 0) throw new ReplayCorruptException("Unsupported replay compression.");
            int n = r.ReadInt();
            if (n < 0 || n > MaxFrames) throw new ReplayCorruptException($"Implausible replay frame count: {n}.");
            _ = ReplayCast.DataFor(p1c);
            _ = ReplayCast.DataFor(p2c);
            _ = ReplayCast.StageFor(stageId);
            int snapLen = r.ReadInt();
            if (snapLen < 0 || snapLen > SnapshotLimits.MaxSnapshotBytes)
                throw new ReplayCorruptException($"Implausible initial snapshot length: {snapLen}.");
            byte[] snapBytes = new byte[snapLen];
            for (int i = 0; i < snapLen; i++) snapBytes[i] = r.ReadByte();
            var probe = new SnapshotReader(snapBytes);
            if (probe.ReadInt() != FightSnapshot.Schema)
                throw new ReplayCorruptException("Initial snapshot schema mismatch.");
            var initial = new FightSnapshot(snapBytes, bypassValidation: true);
            var p1 = new InputState[n];
            var p2 = new InputState[n];
            for (int i = 0; i < n; i++) { p1[i] = UnpackFrame(r); p2[i] = UnpackFrame(r); }
            if (!r.End) throw new ReplayCorruptException("Replay v2 trailing bytes.");
            return new ReplayV2(seed, stageId, p1c, p2c, (ReplayPlatform)plat, initial, p1, p2);
        }
        catch (InvalidOperationException ex) when (ex is not ReplayCorruptException)
        {
            throw new ReplayCorruptException(ex.Message, ex);
        }
    }

    private static ulong ReadFooter(byte[] bytes, int bodyLen)
    {
        ulong v = 0;
        for (int k = 0; k < 8; k++) v |= (ulong)bytes[bodyLen + k] << (8 * k);
        return v;
    }

    /// <summary>Resimulates on a fresh fight (cast + stage from the header) and
    /// returns per-frame snapshot hashes for comparison.</summary>
    /// <param name="roundTimer">Round timer for the fresh sim.</param>
    /// <returns>Per-frame hashes.</returns>
    public ulong[] PlayAll(int roundTimer = 80 * 60)
    {
        var sim = new FightSimulation(_seed,
            ReplayCast.DataFor(_p1Char), ReplayCast.DataFor(_p2Char),
            roundTimer, ReplayCast.StageFor(_stageId));
        var hashes = new ulong[_p1.Length];
        for (int i = 0; i < _p1.Length; i++)
        {
            sim.Step(_p1[i], _p2[i]);
            hashes[i] = sim.Capture().Hash;
        }
        return hashes;
    }
}
