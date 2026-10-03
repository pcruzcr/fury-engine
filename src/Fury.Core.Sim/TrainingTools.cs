namespace Fury.Core.Sim;

/// <summary>What the training dummy does with its inputs.</summary>
public enum DummyMode
{
    /// <summary>Stands still (takes every hit).</summary>
    Stand = 0,

    /// <summary>Holds down.</summary>
    Crouch = 1,

    /// <summary>Jumps again and again.</summary>
    Jump = 2,

    /// <summary>Guards standing (back) against attacks and projectiles, and keeps guarding while in blockstun: stops high and mid.</summary>
    BlockHigh = 3,

    /// <summary>Crouches, and guards down-back against attacks and projectiles: stops low and mid.</summary>
    BlockLow = 4,

    /// <summary>Plays like the CPU opponent (needs a brain).</summary>
    Cpu = 5,
}

/// <summary>
/// Configurable practice dummy. Switchable at runtime; only meant for offline training, where the inputs are not
/// recorded or replayed, so the mode is not part of any deterministic state.
/// </summary>
public sealed class TrainingDummy(IInputProvider? cpu = null) : IInputProvider
{
    /// <summary>Frames before an attack becomes active at which a guarding dummy starts holding back.</summary>
    private const int GuardLeadFrames = 2;

    private FightSimulation? _sim;
    private int _slot;

    public DummyMode Mode { get; set; }

    /// <summary>
    /// Lets the guard modes read the opponent's move timing, so they hold back only around the active frames. Without it they
    /// guard during the whole attack animation, which makes the dummy walk out of range. Call again after a new round.
    /// </summary>
    public void Attach(FightSimulation sim, int slot)
    {
        _sim = sim;
        _slot = slot;
    }

    /// <summary>Advances to the next mode (wraps), skipping <see cref="DummyMode.Cpu"/> when no brain was supplied.</summary>
    public DummyMode Cycle()
    {
        int modes = Enum.GetValues<DummyMode>().Length;
        do
        {
            Mode = (DummyMode)(((int)Mode + 1) % modes);
        }
        while (Mode == DummyMode.Cpu && cpu == null);
        return Mode;
    }

    public InputState Poll(int frame, in FightView view)
    {
        // "Back" is away from the opponent: facing +1 means the opponent is to the right. Guarding is reactive (only around an
        // attack's active frames, while a projectile is out, and while in blockstun) because holding back all the time would
        // just walk out of range.
        var back = view.MyFacing > 0 ? InputButton.Left : InputButton.Right;
        bool threatened = view.OpponentFireballLive || view.MyStateId == FighterStateId.Blockstun || OpponentAboutToHit(in view);
        return Mode switch
        {
            DummyMode.Crouch => new InputState((ushort)InputButton.Down),
            DummyMode.Jump => new InputState((ushort)InputButton.Up),
            DummyMode.BlockHigh => new InputState(threatened ? (ushort)back : (ushort)0),
            DummyMode.BlockLow => new InputState((ushort)(threatened ? back | InputButton.Down : InputButton.Down)),
            DummyMode.Cpu when cpu != null => cpu.Poll(frame, in view),
            _ => new InputState(0),
        };
    }

    private bool OpponentAboutToHit(in FightView view)
    {
        if (view.OpponentStateId != FighterStateId.Attack) return false;
        if (_sim == null) return true;

        int opponent = 1 - _slot;
        var move = _sim.Data(opponent).Moves.GetMove(_sim.State.GetMoveIndex(opponent));
        int frame = _sim.State.GetStateFrame(opponent);
        return frame >= move.StartupFrames - GuardLeadFrames && frame <= move.StartupFrames + move.ActiveFrames;
    }
}

/// <summary>Frame advantage measured after an attack connected.</summary>
/// <param name="Attacker">Fighter that connected (0 or 1).</param>
/// <param name="Blocked">True when the defender blocked, false when it was hit.</param>
/// <param name="Frames">Positive = the attacker can act first by that many frames; negative = the defender can.</param>
/// <param name="Knockdown">The hit launched or knocked the defender down, so there is no meaningful advantage.</param>
public readonly record struct AdvantageResult(int Attacker, bool Blocked, int Frames, bool Knockdown);

/// <summary>
/// Measures frame advantage live: from the frame an attack connects it counts how long each fighter takes to return to a
/// neutral state; the difference is the advantage. Reads the state only. Call <see cref="Update"/> once per simulation frame.
/// </summary>
public sealed class FrameAdvantageTracker
{
    private const int TimeoutFrames = 180;

    private readonly int[] _previousStun = new int[2];
    private bool _measuring;
    private int _attacker;
    private int _defender;
    private bool _blocked;
    private int _elapsed;
    private int _attackerFree;
    private int _defenderFree;

    /// <summary>The most recent completed measurement of a hit, if any.</summary>
    public AdvantageResult? LastHit { get; private set; }

    /// <summary>The most recent completed measurement of a block, if any.</summary>
    public AdvantageResult? LastBlock { get; private set; }

    public void Reset()
    {
        _measuring = false;
        _previousStun[0] = _previousStun[1] = 0;
        LastHit = LastBlock = null;
    }

    public void Update(FightState s)
    {
        bool connected = false;
        for (int d = 0; d < 2; d++)
        {
            int hitstun = s.GetHitstun(d);
            int blockstun = s.GetBlockstun(d);
            int stun = System.Math.Max(hitstun, blockstun);
            if (stun > _previousStun[d])
            {
                Begin(s, defender: d, blocked: blockstun > hitstun);
                connected = true;
            }
            _previousStun[d] = stun;
        }

        if (!_measuring || connected) return; // the connect frame itself is frame 0 of the measurement

        if (s.GetStateId(_defender) is FighterStateId.Knockdown or FighterStateId.Ko || s.IsAirborne(_defender))
        {
            Finish(new AdvantageResult(_attacker, _blocked, 0, Knockdown: true));
            return;
        }

        _elapsed++;
        if (_attackerFree < 0 && FighterStateId.IsNeutral(s.GetStateId(_attacker))) _attackerFree = _elapsed;
        if (_defenderFree < 0 && FighterStateId.IsNeutral(s.GetStateId(_defender))) _defenderFree = _elapsed;

        if (_attackerFree >= 0 && _defenderFree >= 0)
        {
            Finish(new AdvantageResult(_attacker, _blocked, _defenderFree - _attackerFree, Knockdown: false));
        }
        else if (_elapsed >= TimeoutFrames)
        {
            _measuring = false; // something unusual (a long stall or a loop): give up without reporting a wrong number
        }
    }

    private void Begin(FightState s, int defender, bool blocked)
    {
        _measuring = true;
        _defender = defender;
        _attacker = 1 - defender;
        _blocked = blocked;
        _elapsed = 0;
        // A projectile can connect after its owner is already free again.
        _attackerFree = FighterStateId.IsNeutral(s.GetStateId(_attacker)) ? 0 : -1;
        _defenderFree = -1;
    }

    private void Finish(AdvantageResult result)
    {
        _measuring = false;
        if (result.Blocked) LastBlock = result;
        else LastHit = result;
    }
}
