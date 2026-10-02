using Fury.Core.Observability;

namespace Fury.Core.Sim;

/// <summary>
/// Zero-allocation event sink: forwards sim events to the tracer immediately (phase 9).
/// When no tracer is attached, emits are no-ops — the sim is identical either way.
/// <para>
/// Rollback-safe: when the netcode resimulates frames that were already simulated, their events
/// were already presented (SFX, VFX, camera shake). <see cref="BeginFrame"/> marks a frame as
/// "live" only the first time it runs, so resimulation never replays hit sounds twice.
/// </para>
/// </summary>
public sealed class SimEventSink
{
    private readonly IFightTracer? _tracer;
    private int _highWaterFrame = -1;
    private bool _live = true;

    public SimEventSink(IFightTracer? tracer) => _tracer = tracer;

    /// <summary>Highest frame whose events have been presented.</summary>
    public int HighWaterFrame => _highWaterFrame;

    /// <summary>Called at the start of every simulated frame (first run or resimulation).</summary>
    public void BeginFrame(int frame)
    {
        _live = frame > _highWaterFrame;
        if (_live) _highWaterFrame = frame;
    }

    /// <summary>Forget presentation history (use after a non-rollback seek such as replay scrubbing).</summary>
    public void Reset()
    {
        _highWaterFrame = -1;
        _live = true;
    }

    public void Emit(int frame, int eventId, ulong payload)
    {
        if (_live) _tracer?.OnSimEvent(frame, eventId, payload);
    }
}

/// <summary>Counters surfaced to observability: how much the netcode had to rewind.</summary>
public sealed class SimStats
{
    public int Rollbacks { get; private set; }
    public int ResimulatedFrames { get; private set; }

    public void RecordRollback(int resimulatedFrames)
    {
        Rollbacks++;
        ResimulatedFrames += resimulatedFrames;
    }
}

/// <summary>Packing of sim event payloads: attacker / defender / value in one 64-bit word.</summary>
public static class SimEventPayload
{
    public static ulong Pack(int a, int b, int value) => ((uint)a << 24) | ((uint)b << 16) | (uint)(value & 0xFFFF);

    public static void Unpack(ulong payload, out int a, out int b, out int value)
    {
        a = (int)((payload >> 24) & 0xFF);
        b = (int)((payload >> 16) & 0xFF);
        value = (int)(payload & 0xFFFF);
    }
}
