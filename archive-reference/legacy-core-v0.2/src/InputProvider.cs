// Fighter.Core — simulation input source contract (REFACTOR+HARDEN, phase 1).
// Simulation Loop Phase 1 (Input Read) may ONLY come from an IInputProvider:
// keyboards, gamepads, netcode, AI and scripted harnesses all adapt to this
// interface. The simulation never touches hardware, walls clocks, or statics.
namespace Fighter.Core;

/// <summary>Phase-1 input source: the ONLY legal origin of per-frame inputs.</summary>
/// <remarks>Implementations must be deterministic for a given frame sequence
/// (same frame ⇒ same inputs) or explicitly document their entropy source.
/// IL2CPP-safe: plain interface, no default members, no generics.</remarks>
public interface IInputProvider
{
    /// <summary>Reads both fighters' inputs for <paramref name="frame"/>.</summary>
    /// <param name="frame">Simulation frame being read (0-based).</param>
    /// <param name="p1">Player 1 input (frame-stamped by the implementation).</param>
    /// <param name="p2">Player 2 input (frame-stamped by the implementation).</param>
    /// <returns>True when inputs are available; false ends the stream (harness/test use).</returns>
    bool TryGetInputs(int frame, out InputState p1, out InputState p2);
}

/// <summary>Scripted provider: replays a fixed input script (tests, harnesses, tools).</summary>
/// <remarks>Reads past the end return false (an ended stream is explicit;
/// callers own the hold policy). Zero allocation after construction.</remarks>
public sealed class ScriptedInputProvider : IInputProvider
{
    private readonly InputState[] _p1;
    private readonly InputState[] _p2;

    /// <summary>Script frame count.</summary>
    public int Count => _p1.Length;

    /// <summary>Wraps pre-built per-fighter streams (lengths must match).</summary>
    /// <param name="p1">Player 1 stream.</param>
    /// <param name="p2">Player 2 stream.</param>
    /// <exception cref="ArgumentNullException">Thrown on null streams.</exception>
    /// <exception cref="ArgumentException">Thrown on length mismatch.</exception>
    public ScriptedInputProvider(InputState[] p1, InputState[] p2)
    {
        if (p1 is null) throw new ArgumentNullException(nameof(p1));
        if (p2 is null) throw new ArgumentNullException(nameof(p2));
        if (p1.Length != p2.Length) throw new ArgumentException("Stream length mismatch.");
        _p1 = p1;
        _p2 = p2;
    }

    /// <summary>Returns script frame <paramref name="frame"/>; false past the end.</summary>
    /// <param name="frame">Simulation frame.</param>
    /// <param name="p1">Player 1 input.</param>
    /// <param name="p2">Player 2 input.</param>
    /// <returns>True inside the script, false past the end.</returns>
    public bool TryGetInputs(int frame, out InputState p1, out InputState p2)
    {
        if (frame < 0 || frame >= _p1.Length)
        {
            p1 = default;
            p2 = default;
            return false;
        }
        p1 = _p1[frame];
        p2 = _p2[frame];
        return true;
    }
}
