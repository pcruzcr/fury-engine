namespace Fury.Core.Observability;

/// <summary>
/// Zero-allocation observability sink for the simulation. Implementations are attached
/// by the presentation layer; the sim itself never depends on a concrete tracer.
/// All methods must be non-throwing and allocation-free on the hot path.
/// </summary>
public interface IFightTracer
{
    /// <summary>Called once per simulated frame with wall-clock-free sim metrics.</summary>
    void OnFrame(in FrameTrace trace);

    /// <summary>Called for auditable simulation events (hits, state changes, energy spend).</summary>
    void OnSimEvent(int frame, int eventId, ulong payload);
}

/// <summary>Snapshot of one simulated frame's key signals. Struct to keep it off the heap.</summary>
public readonly record struct FrameTrace(
    int Frame,
    int Rollbacks,
    int ResimulatedFrames,
    ulong StateHash);
