using System.Text;
using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Presentation;

/// <summary>
/// Training-mode readout: player 1's input history in numpad notation, the frame data of the last move, the measured
/// frame advantage and the dummy's mode. It only reads the simulation; text is rebuilt when a value changes, not every frame.
/// </summary>
public sealed partial class TrainingOverlay : CanvasLayer
{
    private const int HistoryLines = 14;

    private readonly (ushort Bits, int Frames)[] _history = new (ushort, int)[HistoryLines];
    private readonly StringBuilder _builder = new();
    private int _historyCount;
    private Label _inputs = null!;
    private Label _info = null!;
    private bool _historyDirty = true;
    private int _lastMove = -1;
    private string _moveText = "";
    private bool _infoDirty = true;
    private DummyMode? _shownMode;
    private AdvantageResult? _shownHit;
    private AdvantageResult? _shownBlock;

    public override void _Ready()
    {
        Layer = 9;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _inputs = new Label { Position = new Vector2(16, 120), MouseFilter = Control.MouseFilterEnum.Ignore };
        _inputs.AddThemeFontSizeOverride("font_size", 18);
        _inputs.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
        _inputs.AddThemeColorOverride("font_outline_color", Colors.Black);
        _inputs.AddThemeConstantOverride("outline_size", 4);
        root.AddChild(_inputs);

        _info = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        _info.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _info.GrowHorizontal = Control.GrowDirection.Both;
        _info.GrowVertical = Control.GrowDirection.Begin;
        _info.OffsetBottom = -14;
        _info.AddThemeFontSizeOverride("font_size", 20);
        _info.AddThemeColorOverride("font_outline_color", Colors.Black);
        _info.AddThemeConstantOverride("outline_size", 5);
        root.AddChild(_info);
    }

    /// <summary>Forgets everything shown (new round, restart).</summary>
    public void Reset()
    {
        _historyCount = 0;
        _historyDirty = true;
        _lastMove = -1;
        _moveText = "";
        _infoDirty = true;
    }

    /// <summary>Call once per simulation frame, after it stepped.</summary>
    public void Update(FightSimulation sim, InputState p1Input, TrainingDummy dummy, FrameAdvantageTracker advantage)
    {
        PushInput(p1Input.Bits);

        var s = sim.State;
        if (s.GetStateId(0) == FighterStateId.Attack && s.GetMoveIndex(0) != _lastMove)
        {
            _lastMove = s.GetMoveIndex(0);
            var move = sim.Data(0).Moves.GetMove(_lastMove);
            _moveText = $"Move {_lastMove}: startup {move.StartupFrames}  active {move.ActiveFrames}  recovery {move.RecoveryFrames}  ({move.TotalFrames} total)  dmg {move.Damage}";
            _infoDirty = true;
        }
        else if (s.GetStateId(0) != FighterStateId.Attack)
        {
            _lastMove = -1;
        }

        if (dummy.Mode != _shownMode || advantage.LastHit != _shownHit || advantage.LastBlock != _shownBlock)
        {
            _shownMode = dummy.Mode;
            _shownHit = advantage.LastHit;
            _shownBlock = advantage.LastBlock;
            _infoDirty = true;
        }

        if (_infoDirty)
        {
            _infoDirty = false;
            _info.Text = $"{_moveText}\nON HIT {Format(_shownHit)}    ON BLOCK {Format(_shownBlock)}\nDUMMY: {Describe(dummy.Mode)}  (F5 / pause menu to change)";
        }

        if (_historyDirty)
        {
            _historyDirty = false;
            _inputs.Text = BuildHistory(s.GetFacing(0));
        }
    }

    /// <summary>Displays an advantage from player 1's point of view: + means player 1 acts first.</summary>
    private static string Format(AdvantageResult? result)
    {
        if (result is not { } r) return "--";
        if (r.Knockdown) return "KD";
        int frames = r.Attacker == 0 ? r.Frames : -r.Frames;
        return frames > 0 ? $"+{frames}" : frames.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Describe(DummyMode mode) => mode switch
    {
        DummyMode.Stand => "stand",
        DummyMode.Crouch => "crouch",
        DummyMode.Jump => "jump",
        DummyMode.BlockHigh => "block (standing)",
        DummyMode.BlockLow => "block (crouching)",
        _ => "CPU",
    };

    private void PushInput(ushort bits)
    {
        if (_historyCount > 0 && _history[0].Bits == bits)
        {
            if (_history[0].Frames < 999) _history[0].Frames++;
            _historyDirty = true;
            return;
        }

        int keep = System.Math.Min(_historyCount, HistoryLines - 1);
        for (int i = keep; i > 0; i--) _history[i] = _history[i - 1];
        _history[0] = (bits, 1);
        _historyCount = keep + 1;
        _historyDirty = true;
    }

    private string BuildHistory(int facing)
    {
        _builder.Clear();
        for (int i = 0; i < _historyCount; i++)
        {
            Write(_builder, _history[i].Bits, facing);
            _builder.Append("   ").Append(_history[i].Frames).Append('\n');
        }
        return _builder.ToString();
    }

    /// <summary>Numpad notation relative to the facing (6 = toward the opponent), then the pressed buttons.</summary>
    public static void Write(StringBuilder builder, ushort bits, int facing)
    {
        var input = (InputButton)bits;
        int x = ((input & InputButton.Right) != 0 ? 1 : 0) - ((input & InputButton.Left) != 0 ? 1 : 0);
        if (facing < 0) x = -x;
        int y = ((input & InputButton.Up) != 0 ? 1 : 0) - ((input & InputButton.Down) != 0 ? 1 : 0);
        builder.Append((char)('0' + 5 + x + (3 * y)));

        if ((input & InputButton.Light) != 0) builder.Append('L');
        if ((input & InputButton.Medium) != 0) builder.Append('M');
        if ((input & InputButton.Heavy) != 0) builder.Append('H');
        if ((input & InputButton.Special) != 0) builder.Append('S');
        if ((input & InputButton.Macro1) != 0) builder.Append('G');
        if ((input & InputButton.Block) != 0) builder.Append('B');
        if ((input & InputButton.Parry) != 0) builder.Append('P');
        if ((input & InputButton.Macro2) != 0) builder.Append('2');
        if ((input & InputButton.Macro3) != 0) builder.Append('X');
        if ((input & InputButton.Macro4) != 0) builder.Append('T');
    }
}
