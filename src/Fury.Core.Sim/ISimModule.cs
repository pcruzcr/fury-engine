namespace Fury.Core.Sim;

/// <summary>
/// An optional rule system plugged into the fight (weapons, transformations, tag teams, stage hazards...).
/// A module owns mutable state, so it must take part in the rollback savestate: its words are appended to
/// every snapshot after the base <see cref="FightState"/> block and restored with it. Modules tick once per
/// frame, in registration order, after combat resolution, so the order is part of the determinism contract.
/// Implementations must be allocation-free in <see cref="Tick"/>, <see cref="Serialize"/> and
/// <see cref="Deserialize"/> and must never read floats, time or random sources.
/// </summary>
public interface ISimModule
{
    /// <summary>Number of int words this module adds to every snapshot (constant for the module's lifetime).</summary>
    int StateWords { get; }

    /// <summary>Advances the module one frame.</summary>
    void Tick(FightState state);

    /// <summary>Writes exactly <see cref="StateWords"/> words.</summary>
    void Serialize(Span<int> dest);

    /// <summary>Reads exactly <see cref="StateWords"/> words written by <see cref="Serialize"/>.</summary>
    void Deserialize(ReadOnlySpan<int> source);
}
