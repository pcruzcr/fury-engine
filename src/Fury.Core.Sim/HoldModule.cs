namespace Fury.Core.Sim;

/// <summary>
/// Grapple holds (Dead or Alive / wrestling style) on top of the core throw. When a throw connects, the victim is held
/// for a window: it mashes attack buttons to escape (the hold's escape threshold), the attacker can press
/// <see cref="InputButton.Macro1"/> again to chain a slam, and if the victim has not escaped when the window runs out
/// the slam lands anyway. The slam deals the hold's bonus damage; an escape cancels it. Hold state is part of the
/// rollback snapshot.
/// </summary>
public sealed class HoldModule : SimModule
{
    private const InputButton MashButtons = InputButton.Light | InputButton.Medium | InputButton.Heavy | InputButton.Special;

    private readonly WrestlingSystem _holds;
    private readonly int _holdIndex;

    public HoldModule(WrestlingSystem holds, int holdIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(holds);
        if ((uint)holdIndex >= (uint)holds.Count) throw new ArgumentOutOfRangeException(nameof(holdIndex));
        _holds = holds;
        _holdIndex = holdIndex;
    }

    public WrestlingSystem Holds => _holds;

    /// <summary>True while a victim is being held.</summary>
    public bool Active => _holds.GetHold(_holdIndex).State == WrestlingSystem.HoldState.Active;

    public override int StateWords => _holds.StateWords;

    public override void OnThrow(int attacker, int defender)
    {
        if (!_holds.TryInitiateHold(_holdIndex, attacker, defender)) return;
        var hold = _holds.GetHold(_holdIndex);
        Sim.State.SetHitstop(defender, hold.MaxEscapeTime); // the victim hangs frozen in the grab
        Sim.EmitEvent(SimEventId.HoldStarted, SimEventPayload.Pack(attacker, defender, hold.MaxEscapeTime));
    }

    public override void Tick(FightState state)
    {
        var hold = _holds.GetHold(_holdIndex);
        if (hold.State != WrestlingSystem.HoldState.Active) return;

        if (Sim.Input(hold.Defender).Pressed(MashButtons)) _holds.Mash(_holdIndex);
        hold = _holds.GetHold(_holdIndex);

        if (hold.EscapeProgress >= hold.EscapeThreshold)
        {
            End(state, hold, slam: false);
            return;
        }

        if (hold.IsChainThrow && Sim.Input(hold.Attacker).Pressed(InputButton.Macro1))
        {
            End(state, hold, slam: true);
            return;
        }

        _holds.Update(); // counts the window down; expiring means the victim failed to escape
        if (_holds.GetHold(_holdIndex).State != WrestlingSystem.HoldState.Active) End(state, hold, slam: true);
    }

    private void End(FightState state, WrestlingSystem.HoldData hold, bool slam)
    {
        _holds.Release(_holdIndex);
        state.SetHitstop(hold.Defender, 0);
        if (slam) state.SetHealth(hold.Defender, state.GetHealth(hold.Defender) - hold.Damage);
        Sim.EmitEvent(SimEventId.HoldEnded, SimEventPayload.Pack(hold.Attacker, hold.Defender, slam ? hold.Damage : 0));
    }

    public override void Serialize(Span<int> dest) => _holds.ExportState(dest);

    public override void Deserialize(ReadOnlySpan<int> source) => _holds.ImportState(source);
}
