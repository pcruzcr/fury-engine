// Fighter.Core — fixed-step clock (F0).
// WHY: wall clock is forbidden in Core (§13). The presenter owns the OS clock and
// passes elapsed *microseconds* (integer). All comparisons here are exact integer math.
namespace Fighter.Core;

/// <summary>60 Hz fixed-step accumulator. Integer microseconds only.</summary>
public sealed class SimulationClock
{
    private long _accumulatedMicros;

    /// <summary>Frames advanced since creation. Monotonic.</summary>
    public long Frame { get; private set; }

    /// <summary>Unconsumed microseconds held by the accumulator.</summary>
    public long PendingMicros => _accumulatedMicros - ElapsedForFrames(Frame);

    private static long ElapsedForFrames(long frames) =>
        frames * SimulationUnits.MicrosPerSecond / SimulationUnits.TickRate;

    /// <summary>
    /// Adds elapsed microseconds; returns how many whole frames are now due.
    /// Exact: a frame is due while acc*60 &gt;= (frames+1)*1_000_000 using 64-bit ints.
    /// </summary>
    public int Advance(long elapsedMicros)
    {
        if (elapsedMicros < 0) throw new ArgumentOutOfRangeException(nameof(elapsedMicros));
        _accumulatedMicros += elapsedMicros;
        int due = 0;
        while (_accumulatedMicros * SimulationUnits.TickRate
               >= (Frame + 1) * SimulationUnits.MicrosPerSecond)
        {
            Frame++;
            due++;
        }
        return due;
    }
}
