using Fury.Core.Observability;

namespace Fury.Core.Sim;

/// <summary>
/// Zero-allocation event sink: forwards sim events to the tracer immediately (phase 9).
/// When no tracer is attached, emits are no-ops — the sim is identical either way.
/// </summary>
public sealed class SimEventSink
{
    private readonly IFightTracer? _tracer;

    public SimEventSink(IFightTracer? tracer) => _tracer = tracer;

    public void Emit(int frame, int eventId, ulong payload)
    {
        _tracer?.OnSimEvent(frame, eventId, payload);
    }
}
