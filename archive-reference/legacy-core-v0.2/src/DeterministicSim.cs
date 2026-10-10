// Fighter.Core — minimal F0 deterministic simulation + snapshot/restore (§33, §79).
//
// SCOPE (honest): F0 proves the deterministic *infrastructure*, not combat.
// The stepper advances frame, integrates positions (pos += vel, Vector2I),
// derives velocity from input direction (so inputs change the future),
// ticks cooldown-style timers down, and steps the RNG once per frame.
// Combat (FSM/AABB/hit/energy) arrives in F1 on top of this exact substrate.
namespace Fighter.Core;

/// <summary>F0 simulation state. Every field here participates in snapshot + hash.</summary>
public sealed class DeterministicSim
{
    /// <summary>Millipixels/frame applied from a held direction. F0 constant, documented.</summary>
    public const int F0Speed = 1000;

    /// <summary>Current frame.</summary>
    public int Frame { get; private set; }
    /// <summary>P1 feet position.</summary>
    public Vector2I P1Pos { get; private set; }
    /// <summary>P1 velocity.</summary>
    public Vector2I P1Vel { get; private set; }
    /// <summary>P2 feet position.</summary>
    public Vector2I P2Pos { get; private set; }
    /// <summary>P2 velocity.</summary>
    public Vector2I P2Vel { get; private set; }
    /// <summary>P1 generic timer.</summary>
    public int P1Timer { get; private set; }
    /// <summary>P2 generic timer.</summary>
    public int P2Timer { get; private set; }
    /// <summary>Round timer, frames remaining.</summary>
    public int RoundTimer { get; private set; }

    private readonly DeterministicRng _rng;
    private readonly RingBuffer<InputState> _p1History;
    private readonly RingBuffer<InputState> _p2History;

    /// <summary>RNG state (snapshot-covered).</summary>
    public ulong RngState => _rng.State;

    /// <summary>Creates the substrate sim with <paramref name="seed"/> and round time.</summary>
    public DeterministicSim(ulong seed, int roundTimer = 60 * 99)
    {
        _rng = new DeterministicRng(seed);
        _p1History = new RingBuffer<InputState>(SimulationUnits.RingCapacity);
        _p2History = new RingBuffer<InputState>(SimulationUnits.RingCapacity);
        RoundTimer = roundTimer;
    }

    /// <summary>Advances exactly one frame (fixed order, no external branches).</summary>
    public void Step(InputState p1, InputState p2)
    {
        // Fixed order, no branches on external state. Inputs only affect velocity.
        _p1History.Push(p1);
        _p2History.Push(p2);

        P1Vel = new Vector2I(p1.DirX * F0Speed, p1.DirY * F0Speed);
        P2Vel = new Vector2I(p2.DirX * F0Speed, p2.DirY * F0Speed);
        P1Pos += P1Vel;
        P2Pos += P2Vel;

        if (P1Timer > 0) P1Timer--;
        if (P2Timer > 0) P2Timer--;
        if (RoundTimer > 0) RoundTimer--;

        _ = _rng.NextULong(); // one draw per frame keeps RNG stream frame-locked
        Frame++;
    }

    /// <summary>Test/scenario helper: sets a generic timer (not a combat path).</summary>
    public void SetTimer(int player, int value)
    {
        if (player == 1) P1Timer = value; else P2Timer = value;
    }

    // ---- Snapshot: manual little-endian layout, SchemaVersion first. ----
    /// <summary>Captures an immutable snapshot (schema 1) with hash.</summary>
    public StateSnapshot Capture() => StateSnapshot.Capture(this);

    /// <summary>Restores a snapshot (schema-checked, bounds-checked, trailing-byte-checked).</summary>
    public void Restore(StateSnapshot snap)
    {
        if (snap.SchemaVersion != StateSnapshot.Schema) throw new SnapshotSchemaException("Snapshot schema mismatch.");
        var r = new SnapshotReader(snap.Payload);
        int schema = r.ReadInt();
        if (schema != StateSnapshot.Schema) throw new SnapshotSchemaException("Snapshot payload schema mismatch.");
        Frame = r.ReadInt();
        P1Pos = new Vector2I(r.ReadInt(), r.ReadInt());
        P1Vel = new Vector2I(r.ReadInt(), r.ReadInt());
        P2Pos = new Vector2I(r.ReadInt(), r.ReadInt());
        P2Vel = new Vector2I(r.ReadInt(), r.ReadInt());
        P1Timer = r.ReadInt(); P2Timer = r.ReadInt(); RoundTimer = r.ReadInt();
        _rng.Restore(r.ReadULong());
        int n1 = r.ReadHistoryCount();
        _p1History.Clear();
        for (int i = 0; i < n1; i++) _p1History.Push(r.ReadInput());
        int n2 = r.ReadHistoryCount();
        _p2History.Clear();
        for (int i = 0; i < n2; i++) _p2History.Push(r.ReadInput());
        if (!r.End) throw new SnapshotSchemaException("Snapshot payload trailing bytes.");
    }

    internal void WriteTo(SnapshotWriter w)
    {
        w.Write(Frame);
        w.Write(P1Pos.X); w.Write(P1Pos.Y);
        w.Write(P1Vel.X); w.Write(P1Vel.Y);
        w.Write(P2Pos.X); w.Write(P2Pos.Y);
        w.Write(P2Vel.X); w.Write(P2Vel.Y);
        w.Write(P1Timer); w.Write(P2Timer); w.Write(RoundTimer);
        w.Write(_rng.State);
        w.Write(_p1History.Count);
        for (int i = 0; i < _p1History.Count; i++) w.Write(_p1History.PeekOldest(i));
        w.Write(_p2History.Count);
        for (int i = 0; i < _p2History.Count; i++) w.Write(_p2History.PeekOldest(i));
    }
}

/// <summary>Immutable captured snapshot: schema + payload + hash (Memento).</summary>
public sealed class StateSnapshot
{
    /// <summary>Wire schema version (mismatch rejected loudly).</summary>
    public const int Schema = 1;
    /// <summary>Schema this payload was captured with.</summary>
    public int SchemaVersion { get; }
    /// <summary>Raw little-endian payload.</summary>
    public byte[] Payload { get; }
    /// <summary>FNV-1a hash of the payload (divergence detection).</summary>
    public ulong Hash { get; }

    private StateSnapshot(byte[] payload)
    {
        SchemaVersion = Schema;
        Payload = payload;
        Hash = StateHasher.Hash(payload);
    }

    /// <summary>Captures a snapshot of <paramref name="sim"/>.</summary>
    public static StateSnapshot Capture(DeterministicSim sim)
    {
        var w = new SnapshotWriter();
        w.Write(Schema);
        sim.WriteTo(w);
        return new StateSnapshot(w.ToArray());
    }
}

internal sealed class SnapshotWriter
{
    private readonly System.Collections.Generic.List<byte> _b;
    public SnapshotWriter(int capacity = 1024) => _b = new System.Collections.Generic.List<byte>(capacity);
    public void Write(int v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 24)); }
    public void Write(ulong v) { for (int i = 0; i < 8; i++) _b.Add((byte)(v >> (8 * i))); }
    public void Write(InputState s) { Write((int)s.Buttons); Write(s.DirX); Write(s.DirY); Write(s.Frame); }
    /// <summary>Writes one raw byte (replay v2 headers).</summary>
    public void WriteByte(byte v) => _b.Add(v);
    /// <summary>Writes a little-endian u16 (replay v2 version).</summary>
    public void WriteUShort(ushort v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); }
    public byte[] ToArray() => _b.ToArray();
}

/// <summary>Wire-format safety limits (R1 hardening). Corrupt/truncated payloads must fail
/// loudly with <see cref="InvalidOperationException"/>, never with raw index exceptions
/// and never with unbounded allocations.</summary>
public static class SnapshotLimits
{
    /// <summary>Maximum accepted snapshot payload (fight snapshots are &lt;1 KB; fuzz-tested).</summary>
    public const int MaxSnapshotBytes = 64 * 1024;
    /// <summary>Maximum accepted input-frame count in any bounded history field.</summary>
    public const int MaxHistoryEntries = 64;
}

internal sealed class SnapshotReader
{
    private readonly byte[] _b; private int _i;
    public SnapshotReader(byte[] b) => _b = b;
    public bool End => _i == _b.Length;

    /// <summary>Current offset (white-box tests locate fields without hardcoding layout).</summary>
    internal int Position => _i;

    public static void CheckLength(byte[] payload)
    {
        if (payload.Length > SnapshotLimits.MaxSnapshotBytes)
            throw new InvalidOperationException($"Snapshot payload too large: {payload.Length}B.");
    }

    private void Require(int n)
    {
        if (_i < 0 || n < 0 || _i + n > _b.Length)
            throw new InvalidOperationException($"Truncated snapshot payload at offset {_i} (need {n}).");
    }

    public int ReadInt()
    {
        Require(4);
        int v = _b[_i] | (_b[_i + 1] << 8) | (_b[_i + 2] << 16) | (_b[_i + 3] << 24);
        _i += 4; return v;
    }
    public ulong ReadULong()
    {
        Require(8);
        ulong v = 0; for (int k = 0; k < 8; k++) v |= (ulong)_b[_i + k] << (8 * k);
        _i += 8; return v;
    }
    /// <summary>Reads one raw byte (replay v2 headers).</summary>
    public byte ReadByte()
    {
        Require(1);
        return _b[_i++];
    }
    /// <summary>Reads a little-endian u16 (replay v2 version).</summary>
    public ushort ReadUShort()
    {
        Require(2);
        ushort v = (ushort)(_b[_i] | (_b[_i + 1] << 8));
        _i += 2; return v;
    }
    public InputState ReadInput() =>
        new((Buttons)ReadInt(), ReadInt(), ReadInt(), ReadInt());

    /// <summary>Bounded history count: rejects absurd counts before any allocation loop.</summary>
    public int ReadHistoryCount()
    {
        int n = ReadInt();
        if (n < 0 || n > SnapshotLimits.MaxHistoryEntries)
            throw new InvalidOperationException($"Implausible history count: {n}.");
        return n;
    }
}
