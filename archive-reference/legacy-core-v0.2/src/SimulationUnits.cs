// Fighter.Core — deterministic simulation units (F0).
// WHAT: single place documenting every unit used by the simulation.
// WHY: Master Prompt §14 — never mix units; no floats in Core without justification.
namespace Fighter.Core;

/// <summary>Authoritative simulation units. All integers. No floats.</summary>
public static class SimulationUnits
{
    /// <summary>Fixed simulation rate. 1 frame = 1/60 s.</summary>
    public const int TickRate = 60;

    /// <summary>Microseconds per frame,exact rational 1_000_000/60. Never precompute as float.</summary>
    public const long MicrosPerSecond = 1_000_000L;

    /// <summary>Position unit: millipixels (1/1000 px). Stored in <see cref="Vector2I"/>.</summary>
    public const int MillipixelsPerPixel = 1000;

    /// <summary>Velocity unit: millipixels per frame. No delta time.</summary>
    public const string VelocityUnit = "millipixels/frame";

    /// <summary>EX resource range 0..3000. Conceptually 100% = 3000, 1% = 30 (§28).</summary>
    public const int ExMax = 3000;

    /// <summary>Resonance range 0..1000 (§29).</summary>
    public const int ResonanceMax = 1000;

    /// <summary>Input history capacity (§17). Do not enlarge without declare/measure/test/document.</summary>
    public const int RingCapacity = 8;
}
