using System.Diagnostics;

namespace Fury.Core.Net;

/// <summary>
/// Round-trip estimator driven by acks: the time between sending frame N and seeing it
/// acknowledged. (The previous estimate restarted a stopwatch on every send and stopped it on
/// the first packet of any kind, which does not measure a round trip.) Smoothed with an integer
/// EWMA (7/8 old + 1/8 new). Thread-safe.
/// </summary>
public sealed class RttEstimator
{
    private const int Ring = 256;

    private readonly object _gate = new();
    private readonly long[] _sentAt = new long[Ring];
    private readonly int[] _sentFrame = new int[Ring];
    private long _smoothedTicks = -1;

    public RttEstimator() => Array.Fill(_sentFrame, -1);

    /// <summary>Records the send time of the newest frame carried by an outgoing packet.</summary>
    public void OnSend(int latestFrame, long timestamp = -1)
    {
        if (latestFrame < 0) return;
        long now = timestamp >= 0 ? timestamp : Stopwatch.GetTimestamp();
        lock (_gate)
        {
            int slot = latestFrame & (Ring - 1);
            if (_sentFrame[slot] != latestFrame)
            {
                _sentFrame[slot] = latestFrame;
                _sentAt[slot] = now; // keep the FIRST send time: retransmissions must not shorten the RTT
            }
        }
    }

    /// <summary>Consumes an advancing ack and updates the smoothed RTT when the frame was timed.</summary>
    public void OnAck(int ackFrame, long timestamp = -1)
    {
        if (ackFrame < 0) return;
        long now = timestamp >= 0 ? timestamp : Stopwatch.GetTimestamp();
        lock (_gate)
        {
            int slot = ackFrame & (Ring - 1);
            if (_sentFrame[slot] != ackFrame) return;
            long sample = now - _sentAt[slot];
            if (sample < 0) return;
            _smoothedTicks = _smoothedTicks < 0 ? sample : (((_smoothedTicks * 7) + sample) / 8);
        }
    }

    /// <summary>Smoothed RTT in milliseconds (0 until the first sample).</summary>
    public double RttMilliseconds
    {
        get
        {
            lock (_gate) { return _smoothedTicks < 0 ? 0 : _smoothedTicks * 1000.0 / Stopwatch.Frequency; }
        }
    }

    /// <summary>Smoothed RTT in simulation ticks (1/60 s), rounded up.</summary>
    public int RttTicks => (int)System.Math.Ceiling(RttMilliseconds * 60.0 / 1000.0);
}
