namespace Fury.Core.Sim;

/// <summary>
/// Finishing moves (Mortal Kombat style fatalities). When a round ends in a KO and a configured finisher is available,
/// the winner gets a time window to enter its button/direction sequence; the loser stays down. A wrong press or a pause
/// longer than <see cref="StepTimeoutFrames"/> restarts the sequence; completing it performs the finisher, which then plays
/// out for its execution duration. Sequences are written for a fighter facing RIGHT (Right = forward) and mirrored
/// automatically when the winner faces left. The presentation keeps feeding the winner's real inputs while
/// <see cref="Phase"/> is <see cref="FinisherPhase.Window"/> or <see cref="FinisherPhase.Executing"/>. The window,
/// progress and timers are part of the rollback snapshot.
/// </summary>
public sealed class FinisherModule : SimModule
{
    /// <summary>Phases of the finisher flow.</summary>
    public enum FinisherPhase
    {
        None = 0,
        Window = 1,
        Executing = 2,
        Done = 3,
    }

    private const int ExtraWords = 5;
    private const int AllInputBits = 0x3FFF;
    private const InputButton AttackAndDirections = (InputButton)AllInputBits;

    private readonly FatalitySystem _fatalities;
    private int _phase;
    private int _winner;
    private int _progress;
    private int _lastStepFrame;
    private int _index;

    public FinisherModule(FatalitySystem fatalities, int stepTimeoutFrames = 45)
    {
        ArgumentNullException.ThrowIfNull(fatalities);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepTimeoutFrames);
        _fatalities = fatalities;
        StepTimeoutFrames = stepTimeoutFrames;
    }

    /// <summary>Frames the winner may wait between two presses of the sequence.</summary>
    public int StepTimeoutFrames { get; }

    public FatalitySystem Fatalities => _fatalities;

    public FinisherPhase Phase => (FinisherPhase)_phase;

    /// <summary>Fighter entering the sequence (valid while a window or execution is running).</summary>
    public int Winner => _winner;

    /// <summary>Correct presses so far.</summary>
    public int Progress => _progress;

    /// <summary>True while the presentation should give the winner real control.</summary>
    public bool WinnerHasControl => Phase is FinisherPhase.Window or FinisherPhase.Executing;

    public override int StateWords => _fatalities.StateWords + ExtraWords;

    public override void Tick(FightState state)
    {
        switch (Phase)
        {
            case FinisherPhase.None:
                TryOpenWindow(state);
                break;
            case FinisherPhase.Window:
                TickWindow(state);
                break;
            case FinisherPhase.Executing:
                _fatalities.Update();
                if (_fatalities.GetFatality(_index).State == FatalitySystem.FatalityState.Completed) _phase = (int)FinisherPhase.Done;
                break;
        }
    }

    private void TryOpenWindow(FightState state)
    {
        if (state.RoundState != 1 || state.Winner < 0) return;
        int loser = 1 - state.Winner;
        if (state.GetHealth(loser) != 0) return; // timeouts and draws have no finisher

        for (int i = 0; i < _fatalities.Count; i++)
        {
            if (!_fatalities.IsAvailable(i, state.GetHealth(state.Winner), state.GetHealth(loser)) || !_fatalities.TryStartInput(i)) continue;
            _phase = (int)FinisherPhase.Window;
            _winner = state.Winner;
            _index = i;
            _progress = 0;
            _lastStepFrame = state.Frame;
            Sim.EmitEvent(SimEventId.FinisherStarted, SimEventPayload.Pack(_winner, i, _fatalities.GetFatality(i).InputWindow));
            return;
        }
    }

    private void TickWindow(FightState state)
    {
        _fatalities.Update();
        var data = _fatalities.GetFatality(_index);
        if (data.State != FatalitySystem.FatalityState.Inputting)
        {
            _phase = (int)FinisherPhase.Done; // the window ran out
            return;
        }

        var input = Sim.Input(_winner);
        var required = Required(data.GetInputAt(_progress), state.GetFacing(_winner));

        if (input.Pressed(required))
        {
            _progress++;
            _lastStepFrame = state.Frame;
            if (_progress >= data.InputLength && _fatalities.TryExecute(_index))
            {
                _phase = (int)FinisherPhase.Executing;
                Sim.EmitEvent(SimEventId.FinisherPerformed, SimEventPayload.Pack(_winner, _index, (int)data.Type));
            }
        }
        else if (input.Pressed(AttackAndDirections & ~required))
        {
            _progress = 0; // wrong press: start over
        }
        else if (_progress > 0 && state.Frame - _lastStepFrame > StepTimeoutFrames)
        {
            _progress = 0;
        }
    }

    /// <summary>Mirrors left/right so one authored sequence works from both sides.</summary>
    private static InputButton Required(int authored, int facing)
    {
        var bits = (InputButton)authored;
        if (facing >= 0) return bits;
        var mirrored = bits & ~(InputButton.Left | InputButton.Right);
        if ((bits & InputButton.Left) != 0) mirrored |= InputButton.Right;
        if ((bits & InputButton.Right) != 0) mirrored |= InputButton.Left;
        return mirrored;
    }

    public override void Serialize(Span<int> dest)
    {
        _fatalities.ExportState(dest);
        int o = _fatalities.StateWords;
        dest[o] = _phase;
        dest[o + 1] = _winner;
        dest[o + 2] = _progress;
        dest[o + 3] = _lastStepFrame;
        dest[o + 4] = _index;
    }

    public override void Deserialize(ReadOnlySpan<int> source)
    {
        _fatalities.ImportState(source);
        int o = _fatalities.StateWords;
        _phase = source[o];
        _winner = source[o + 1];
        _progress = source[o + 2];
        _lastStepFrame = source[o + 3];
        _index = source[o + 4];
    }
}
