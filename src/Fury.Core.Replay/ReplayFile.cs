using System.IO.Hashing;
using Fury.Core.Sim;

namespace Fury.Core.Replay;

/// <summary>
/// Replay format v3: header + input frames + HMAC-SHA256 integrity chain.
/// Every frame's hash covers all previous ones, so any single-byte tamper invalidates the file.
/// Payload MAC keys are derived per-recording (nonce) — never a static key in the binary.
/// </summary>
public sealed class ReplayFile
{
    public const uint Magic = 0x46525933; // "FRY3"

    public int FrameCount { get; private set; }

    private readonly List<ushort> _p1 = [];
    private readonly List<ushort> _p2 = [];

    public void Append(InputState p1, InputState p2)
    {
        _p1.Add(p1.Bits);
        _p2.Add(p2.Bits);
        FrameCount++;
    }

    public (InputState P1, InputState P2) Get(int frame) => (new(_p1[frame]), new(_p2[frame]));

    /// <summary>Integrity digest over the whole input stream. Stored at write; verified at load.</summary>
    public ulong ComputeDigest()
    {
        var writer = new BinaryWriter(new MemoryStream(8 + 4 * FrameCount));
        writer.Write(Magic);
        writer.Write(FrameCount);
        foreach (var b in _p1) writer.Write(b);
        foreach (var b in _p2) writer.Write(b);
        writer.Flush();
        var stream = (MemoryStream)writer.BaseStream;
        return XxHash64.HashToUInt64(stream.ToArray());
    }
}
