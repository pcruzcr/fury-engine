using Fury.Core.Authoring;
using Fury.Core.Data;
using Fury.Core.Math;
using Fury.Core.Sim;
using Fury.Game.Content;
using Fury.Game.Presentation;
using Fury.Game.Stages;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// The artist's bench: pick a character (and stage), pick any state or move, scrub or play its frames and see exactly what the game
/// shows, in the 2D, 2.5D and 3D views, with the hurtbox, hitbox, pivot and floor drawn over the art. The art goes through the same
/// code path as a real fight, so what lines up here lines up in the game. Press R to rescan the content folders after saving a
/// file, without restarting. Keys: Space play/pause, Left/Right step, B boxes, F flip, Tab view, R reload.
/// </summary>
public partial class CharacterViewer : Control
{
    private sealed record PoseChoice(string Label, int State, int Move, bool Crouch, bool Air, int Frames);

    private const int IdleScrubFrames = 90;

    private FightSimulation _sim = null!;
    private FightPresentation _view = null!;
    private ViewMode _mode = ViewMode.Flat2D;
    private IReadOnlyList<CharacterEntry> _characters = [];
    private readonly List<PoseChoice> _poses = [];
    private IReadOnlyList<string> _stageNames = [];

    private OptionButton _characterPicker = null!;
    private OptionButton _posePicker = null!;
    private OptionButton _stagePicker = null!;
    private OptionButton _viewPicker = null!;
    private HSlider _frameSlider = null!;
    private CheckBox _play = null!;
    private CheckBox _boxes = null!;
    private CheckBox _flip = null!;
    private Label _info = null!;
    private Control _pivotMarker = null!;
    private int _frame;
    private int _tick;
    private bool _suppress;
    private string? _capturePath;
    private int _captureAfter = -1;

    public override void _Ready()
    {
        ContentLibrary.IncludeTemplates = true; // the bench is where templates are worked on
        ContentLibrary.Reload();
        BuildUi();
        Rebuild();
        ApplyCommandLine();
    }

    /// <summary>
    /// Dev / CI hooks (after <c>--</c>): <c>--fury-templates</c>, <c>--fury-character=id</c>, <c>--fury-view=0|1|2</c>,
    /// <c>--fury-pose=text</c> (first pose whose label contains it), <c>--fury-frame=n</c> (freezes on that frame),
    /// <c>--fury-frames=n --fury-capture=file.png</c> (screenshot after n ticks, then quit).
    /// </summary>
    private void ApplyCommandLine()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--fury-character=", StringComparison.Ordinal))
            {
                int index = _characters.ToList().FindIndex(c => c.Id == arg[17..]);
                if (index >= 0)
                {
                    _characterPicker.Selected = index;
                    Rebuild();
                }
            }
            else if (arg.StartsWith("--fury-view=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(12), out int view) && view is >= 0 and <= 2)
            {
                _mode = (ViewMode)view;
                Rebuild();
            }
            else if (arg.StartsWith("--fury-pose=", StringComparison.Ordinal))
            {
                int index = _poses.FindIndex(p => p.Label.Contains(arg[12..], StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _posePicker.Selected = index;
                    OnPoseChanged();
                }
            }
            else if (arg.StartsWith("--fury-frame=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(13), out int frame))
            {
                _play.ButtonPressed = false;
                _frame = frame;
                Apply();
            }
            else if (arg.StartsWith("--fury-capture=", StringComparison.Ordinal)) _capturePath = arg[15..];
            else if (arg.StartsWith("--fury-frames=", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(14), out int after)) _captureAfter = after;
        }
    }

    // ------------------------------------------------------------------ ui

    private void BuildUi()
    {
        // A canvas layer of its own: the 2D view's camera would otherwise pan and zoom the controls with the world.
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        var panel = new PanelContainer { Name = "Controls" };
        panel.SetAnchorsPreset(LayoutPreset.TopRight);
        panel.GrowHorizontal = GrowDirection.Begin;
        panel.OffsetRight = -12;
        panel.OffsetTop = 12;
        layer.AddChild(panel);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);

        _characterPicker = Picker(column, "Character", _ => OnCharacterChanged());
        _posePicker = Picker(column, "State / move", _ => OnPoseChanged());
        _stagePicker = Picker(column, "Stage", _ => Rebuild());
        _viewPicker = Picker(column, "View", i => { _mode = (ViewMode)(int)i; Rebuild(); });
        _viewPicker.AddItem("2D (flat)", (int)ViewMode.Flat2D);
        _viewPicker.AddItem("2.5D (perspective)", (int)ViewMode.Perspective2_5D);
        _viewPicker.AddItem("3D (cinematic)", (int)ViewMode.Cinematic3D);

        _frameSlider = new HSlider { MinValue = 0, MaxValue = 1, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(180, 24) };
        _frameSlider.ValueChanged += v =>
        {
            if (_suppress) return;
            _frame = (int)v;
            Apply();
        };
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Frame", CustomMinimumSize = new Vector2(90, 0) });
        row.AddChild(_frameSlider);
        column.AddChild(row);

        _play = new CheckBox { Text = "Play (Space)", ButtonPressed = true };
        _boxes = new CheckBox { Text = "Hurtbox / hitbox / pivot (B)", ButtonPressed = true };
        _flip = new CheckBox { Text = "Face left (F)" };
        _boxes.Toggled += _ => Apply();
        _flip.Toggled += _ => Apply();
        column.AddChild(_play);
        column.AddChild(_boxes);
        column.AddChild(_flip);

        var reload = new Button { Text = "Reload art from disk (R)" };
        reload.Pressed += Reload;
        column.AddChild(reload);
        var back = new Button { Text = "Back (Esc)" };
        back.Pressed += () => GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
        column.AddChild(back);

        _info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _info.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(_info);

        // A crosshair at the fighter's feet (the pivot): the point the art's pivot must land on.
        _pivotMarker = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _pivotMarker.Draw += () =>
        {
            _pivotMarker.DrawLine(new Vector2(-14, 0), new Vector2(14, 0), Colors.Cyan, 2f);
            _pivotMarker.DrawLine(new Vector2(0, -14), new Vector2(0, 14), Colors.Cyan, 2f);
        };
        layer.AddChild(_pivotMarker);
    }

    private OptionButton Picker(Container parent, string label, Action<long> onSelected)
    {
        var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        picker.ItemSelected += i =>
        {
            if (!_suppress) onSelected(i);
        };
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
        row.AddChild(picker);
        parent.AddChild(row);
        return picker;
    }

    // ------------------------------------------------------------------ content

    private void Reload()
    {
        ContentLibrary.Reload();
        Rebuild();
    }

    private void Rebuild()
    {
        _suppress = true;
        string keepCharacter = _characterPicker.ItemCount > 0 ? _characters[_characterPicker.Selected].Id : "";
        _characters = ContentLibrary.Characters;
        _characterPicker.Clear();
        int selected = 0;
        for (int i = 0; i < _characters.Count; i++)
        {
            _characterPicker.AddItem(_characters[i].DisplayName);
            if (_characters[i].Id == keepCharacter) selected = i;
        }
        _characterPicker.Selected = selected;

        string keepStage = _stagePicker.ItemCount > 0 ? _stagePicker.GetItemText(_stagePicker.Selected) : "";
        _stageNames = StageLoader.List();
        _stagePicker.Clear();
        _stagePicker.AddItem("(default arena)");
        int stageSelected = 0;
        foreach (string name in _stageNames)
        {
            _stagePicker.AddItem(name);
            if (name == keepStage) stageSelected = _stagePicker.ItemCount - 1;
        }
        _stagePicker.Selected = stageSelected;
        if (_viewPicker.Selected < 0) _viewPicker.Selected = 0;
        _viewPicker.Selected = (int)_mode;

        int keepPose = _posePicker.Selected;
        int keepFrame = _frame;
        BuildSimulation();
        BuildPoseList();
        if (keepPose >= 0 && keepPose < _poses.Count) _posePicker.Selected = keepPose; // changing the view or reloading keeps what you were looking at
        _suppress = false;
        OnPoseChanged();
        if (keepPose >= 0 && keepPose < _poses.Count)
        {
            _frame = System.Math.Min(keepFrame, (int)_frameSlider.MaxValue); // a reload or a view change must not rewind what you were studying
            _suppress = true;
            _frameSlider.Value = _frame;
            _suppress = false;
            Apply();
        }
    }

    private void BuildSimulation()
    {
        var config = CharacterRoster.Create(_characters[_characterPicker.Selected].Id);
        var rules = FightConfig.Default();
        PlayableStage? stage = null;
        if (_stagePicker.Selected > 0 && StageLoader.TryLoad(_stageNames[_stagePicker.Selected - 1]) is { } loaded) stage = StageLoader.Build(loaded, rules);

        var module = stage?.CreateModule();
        _sim = new FightSimulation(config, config, stage?.Config ?? rules, modules: module != null ? [module] : null);

        if (_view != null)
        {
            RemoveChild(_view);
            _view.QueueFree();
        }

        _view = _mode == ViewMode.Flat2D ? new FightPresentation2D() : new FightPresentation3D { Cinematic = _mode == ViewMode.Cinematic3D };
        _view.StageBackground = stage?.Background;
        _view.StageArt = stage?.Art ?? StageArtSet.Default;
        AddChild(_view);
        MoveChild(_view, 0);
        _view.Bind(_sim);
    }

    private void BuildPoseList()
    {
        _poses.Clear();
        _posePicker.Clear();

        void Add(string label, int state, bool crouch = false, bool air = false, int move = -1, int frames = IdleScrubFrames) =>
            _poses.Add(new PoseChoice(label, state, move, crouch, air, frames));

        Add("idle", FighterStateId.Idle);
        Add("walk forward", FighterStateId.WalkForward);
        Add("walk back", FighterStateId.WalkBack);
        Add("crouch", FighterStateId.Crouch, crouch: true);
        Add("jump", FighterStateId.Jump, air: true);
        Add("dash", FighterStateId.Dash);
        Add("backdash", FighterStateId.Backdash, air: true);
        Add("hit (standing)", FighterStateId.Hitstun, frames: 30);
        Add("hit (crouching)", FighterStateId.Hitstun, crouch: true, frames: 30);
        Add("hit (in the air)", FighterStateId.Hitstun, air: true, frames: 30);
        Add("block (standing)", FighterStateId.Blockstun, frames: 30);
        Add("block (crouching)", FighterStateId.Blockstun, crouch: true, frames: 30);
        Add("knockdown", FighterStateId.Knockdown, frames: 60);
        Add("ko", FighterStateId.Ko, frames: 60);

        var entry = _characters[_characterPicker.Selected];
        var moves = _sim.Data(0).Moves;
        for (int m = 0; m < moves.MoveCount; m++)
        {
            var move = moves.GetMove(m);
            bool crouch = ((move.Command >> 24) & 3) == 2;
            string name = AnimationNames.MoveName(entry.Manifest?.MoveNames, m);
            Add($"move {m}: {name}", FighterStateId.Attack, crouch, move.Has(MoveTraits.Air), m, move.TotalFrames + 1);
        }

        foreach (var pose in _poses) _posePicker.AddItem(pose.Label);
        _posePicker.Selected = 0;
    }

    // ------------------------------------------------------------------ playback

    private void OnCharacterChanged()
    {
        Rebuild();
    }

    private void OnPoseChanged()
    {
        if (_posePicker.Selected < 0) return;
        var pose = _poses[_posePicker.Selected];
        _frame = 0;
        _suppress = true;
        _frameSlider.MaxValue = System.Math.Max(1, pose.Frames - 1);
        _frameSlider.Value = 0;
        _suppress = false;
        Apply();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_sim == null || _posePicker.Selected < 0) return;
        if (_play.ButtonPressed)
        {
            var pose = _poses[_posePicker.Selected];
            _frame = (_frame + 1) % System.Math.Max(1, pose.Frames);
            _suppress = true;
            _frameSlider.Value = _frame;
            _suppress = false;
        }
        Apply();

        if (_capturePath != null && _captureAfter >= 0 && _tick >= _captureAfter)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(_capturePath);
            GD.Print(error == Error.Ok ? $"[fury] captured {_capturePath}" : $"[fury] capture failed: {error}");
            GetTree().Quit();
        }
    }

    /// <summary>Puts fighter 0 in the chosen state and frame and draws it exactly as a fight would.</summary>
    private void Apply()
    {
        if (_sim == null || _posePicker.Selected < 0) return;

        // The clock drives looping animations; a neutral step advances it. The forced pose below overrides whatever it did.
        _sim.Step(new InputState(0), new InputState(0));
        _tick++;

        var pose = _poses[_posePicker.Selected];
        var s = _sim.State;
        s.SetX(0, Fp.FromInt(-7)); // the controls cover the right side of the screen: the subject stays on the left
        s.SetX(1, Fp.FromInt(7));
        s.SetY(0, pose.Air ? Fp.FromInt(-2) : Fp.Zero);
        s.SetY(1, Fp.Zero);
        s.SetFacing(0, _flip.ButtonPressed ? -1 : 1);
        s.SetFacing(1, -1);
        s.SetHealth(0, _sim.Config.MaxHealth);
        s.SetHealth(1, _sim.Config.MaxHealth);
        PoseSetup.Apply(_sim, 0, pose.State, _frame, pose.Crouch, pose.Air, pose.Move, pose.Air ? Fp.FromRatio(-4, 10) : null);
        PoseSetup.Apply(_sim, 1, FighterStateId.Idle, 0);
        s.SetStateFrame(0, _frame);

        _view.ShowBoxes = _boxes.ButtonPressed;
        _view.Sync();

        _pivotMarker.Visible = _boxes.ButtonPressed && _mode == ViewMode.Flat2D;
        if (_pivotMarker.Visible)
        {
            var point = WorldProjection.ToScreen(s.GetX(0), s.GetY(0));
            _pivotMarker.Position = GetViewport().GetCanvasTransform() * point;
            _pivotMarker.QueueRedraw();
        }

        string playing = _view.CurrentAnimation(0) ?? "procedural fighter";
        _info.Text = $"{pose.Label}\nstate frame {_frame}/{System.Math.Max(0, pose.Frames - 1)}\nanimation: {playing}";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Space:
                _play.ButtonPressed = !_play.ButtonPressed;
                break;
            case Key.Left or Key.Right when !_play.ButtonPressed:
                var pose = _poses[_posePicker.Selected];
                int count = System.Math.Max(1, pose.Frames);
                _frame = (_frame + (key.Keycode == Key.Right ? 1 : count - 1)) % count;
                _suppress = true;
                _frameSlider.Value = _frame;
                _suppress = false;
                Apply();
                break;
            case Key.B:
                _boxes.ButtonPressed = !_boxes.ButtonPressed;
                break;
            case Key.F:
                _flip.ButtonPressed = !_flip.ButtonPressed;
                break;
            case Key.Tab:
                _mode = (ViewMode)(((int)_mode + 1) % 3);
                Rebuild();
                break;
            case Key.R:
                Reload();
                break;
            case Key.Escape:
                GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
                break;
        }
    }
}
