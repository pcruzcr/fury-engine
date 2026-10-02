using Fury.Core.Observability;
using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>One simulation event queued for presentation.</summary>
public readonly record struct FightEvent(int Frame, int Id, ulong Payload)
{
    public void Unpack(out int a, out int b, out int value) => SimEventPayload.Unpack(Payload, out a, out b, out value);
}

/// <summary>
/// Godot-side <see cref="IFightTracer"/>: stores events in a fixed ring (no allocation per event) so the
/// director can drain them after each simulation step and trigger VFX/SFX/camera. Because the simulation
/// only emits an event the FIRST time a frame runs (see <c>SimEventSink.BeginFrame</c>), resimulation
/// during rollback never queues a hit sound twice. Optional console logging is off by default.
/// </summary>
public sealed class FightEventQueue : IFightTracer
{
    private const int Capacity = 256;

    private readonly FightEvent[] _ring = new FightEvent[Capacity];
    private int _head;
    private int _count;
    private readonly bool _logEvents;
    private readonly int _frameLogInterval;

    public FightEventQueue(bool logEvents = false, int frameLogInterval = 0)
    {
        _logEvents = logEvents;
        _frameLogInterval = frameLogInterval;
    }

    /// <summary>Last frame trace (rollback counters and state hash for the debug overlay).</summary>
    public FrameTrace LastFrame { get; private set; }

    public int Pending => _count;

    public void OnFrame(in FrameTrace trace)
    {
        LastFrame = trace;
        if (_frameLogInterval > 0 && trace.Frame % _frameLogInterval == 0)
        {
            GD.Print($"[Frame {trace.Frame}] rollbacks={trace.Rollbacks} resim={trace.ResimulatedFrames} hash={trace.StateHash:x16}");
        }
    }

    public void OnSimEvent(int frame, int eventId, ulong payload)
    {
        if (_logEvents) GD.Print($"[Frame {frame}] event {eventId} payload {payload}");

        if (_count == Capacity)
        {
            _head = (_head + 1) % Capacity; // overwrite the oldest: presentation must never block the sim
            _count--;
        }
        _ring[(_head + _count) % Capacity] = new FightEvent(frame, eventId, payload);
        _count++;
    }

    /// <summary>Dequeues the oldest event; false when empty.</summary>
    public bool TryDequeue(out FightEvent evt)
    {
        if (_count == 0)
        {
            evt = default;
            return false;
        }
        evt = _ring[_head];
        _head = (_head + 1) % Capacity;
        _count--;
        return true;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}
