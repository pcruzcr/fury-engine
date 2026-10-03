using Fury.Core.Authoring;
using Fury.Core.Data;
using Fury.Core.Sim;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Visual frame data editor for the character move tables (FMT2). Every field the simulation reads is editable:
/// timings, damage, stuns, hit level, traits, command (buttons + motion + stance), cancels, launch, meter cost,
/// priority and projectile parameters, with the on-hit / on-block advantage computed live. Saving writes
/// <c>user://characters/&lt;id&gt;.fmt2</c>; the game loads those files at boot (see <see cref="CharacterMods"/>), so an
/// edit is one save and a restart away from being playable. All edits go through <see cref="MoveTableDocument"/>,
/// which validates them.
/// </summary>
public partial class FrameDataEditor : Control
{
    private sealed record IntField(string Label, double Min, double Max, Func<MoveEntry, int> Get, Func<MoveEntry, int, MoveEntry> Set);

    /// <summary>A Q16.16 field shown in simulation units.</summary>
    private sealed record UnitField(string Label, double Min, double Max, Func<MoveEntry, int> Get, Func<MoveEntry, int, MoveEntry> Set);

    private static readonly IntField[] TimingFields =
    [
        new("Startup frames", 1, 100, m => m.StartupFrames, (m, v) => m with { StartupFrames = v }),
        new("Active frames", 1, 100, m => m.ActiveFrames, (m, v) => m with { ActiveFrames = v }),
        new("Recovery frames", 0, 100, m => m.RecoveryFrames, (m, v) => m with { RecoveryFrames = v }),
        new("Damage", 0, 1000, m => m.Damage, (m, v) => m with { Damage = v }),
        new("Hitstop frames", 0, 50, m => m.HitstopFrames, (m, v) => m with { HitstopFrames = v }),
        new("Hitstun frames (0 = legacy)", 0, 200, m => m.HitstunFrames, (m, v) => m with { HitstunFrames = v }),
        new("Blockstun frames (0 = legacy)", 0, 200, m => m.BlockstunFrames, (m, v) => m with { BlockstunFrames = v }),
        new("Category (0 normal .. 4 throw)", 0, 7, m => m.Category, (m, v) => m with { Category = v }),
        new("Priority", -100, 100, m => m.Priority, (m, v) => m with { Priority = v }),
        new("Meter cost (EX points)", 0, 300, m => m.MeterCost, (m, v) => m with { MeterCost = v }),
        new("Projectile lifetime", 0, 600, m => m.ProjectileLifetime, (m, v) => m with { ProjectileLifetime = v }),
        new("Projectile spawn frame", 0, 100, m => m.ProjectileSpawnFrame, (m, v) => m with { ProjectileSpawnFrame = v }),
    ];

    private static readonly UnitField[] GeometryFields =
    [
        new("Pushback (units/frame)", -10, 10, m => m.PushbackRaw, (m, v) => m with { PushbackRaw = v }),
        new("Reach (units)", 0, 20, m => m.ReachRaw, (m, v) => m with { ReachRaw = v }),
        new("Box half height (units)", 0, 10, m => m.BoxHalfHeightRaw, (m, v) => m with { BoxHalfHeightRaw = v }),
        new("Box centre height (units)", -10, 10, m => m.BoxCenterYRaw, (m, v) => m with { BoxCenterYRaw = v }),
        new("Launch speed (units/frame)", 0, 5, m => m.LaunchVyRaw, (m, v) => m with { LaunchVyRaw = v }),
        new("Projectile speed (units/frame)", -5, 5, m => m.ProjectileSpeedRaw, (m, v) => m with { ProjectileSpeedRaw = v }),
    ];

    private static readonly (string Label, MoveTraits Trait)[] TraitBoxes =
    [
        ("Air", MoveTraits.Air),
        ("Launcher", MoveTraits.Launcher),
        ("Knockdown", MoveTraits.Knockdown),
        ("Throw", MoveTraits.Throw),
        ("Projectile", MoveTraits.Projectile),
        ("Unblockable", MoveTraits.Unblockable),
    ];

    private static readonly (string Label, InputButton Button)[] ButtonBoxes =
    [
        ("Light", InputButton.Light),
        ("Medium", InputButton.Medium),
        ("Heavy", InputButton.Heavy),
        ("Special", InputButton.Special),
        ("Macro1 (grab)", InputButton.Macro1),
        ("Macro2 (super)", InputButton.Macro2),
        ("Macro3 (power)", InputButton.Macro3),
        ("Macro4 (tag)", InputButton.Macro4),
    ];

    private static readonly string[] CancelNames = ["Normal", "Command normal", "Special", "Super", "Throw", "Cat 5", "Cat 6", "Cat 7"];

    private CharacterConfig? _character;
    private MoveTableDocument? _document;
    private int _selected = -1;
    private bool _syncing; // true while SelectMove pushes values into the widgets

    private OptionButton _characterSelector = null!;
    private OptionButton _moveSelector = null!;
    private LineEdit _nameHash = null!;
    private readonly List<(IntField Field, SpinBox Box)> _intBoxes = [];
    private readonly List<(UnitField Field, SpinBox Box)> _unitBoxes = [];
    private readonly List<(MoveTraits Trait, CheckBox Box)> _traitChecks = [];
    private readonly List<(InputButton Button, CheckBox Box)> _buttonChecks = [];
    private readonly List<CheckBox> _cancelChecks = [];
    private OptionButton _level = null!;
    private OptionButton _motion = null!;
    private OptionButton _stance = null!;
    private Label _summary = null!;
    private Label _status = null!;

    private static string SaveDirectory => ProjectSettings.GlobalizePath(CharacterMods.Folder);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
    }

    public override void _Ready()
    {
        CharacterMods.LoadAll();
        BuildUi();
        LoadCharacter(CharacterRoster.Ids[0]);
        RefreshMoveList();
        SelectMove(0);
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var background = new ColorRect { Color = new Color(0.07f, 0.07f, 0.12f), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(root);

        root.AddChild(new Label { Text = "Fury Engine - Frame Data Editor (FMT2)", HorizontalAlignment = HorizontalAlignment.Center });

        var pickers = new HBoxContainer();
        root.AddChild(pickers);
        pickers.AddChild(new Label { Text = "Character:" });
        _characterSelector = new OptionButton();
        foreach (string id in CharacterRoster.Ids) _characterSelector.AddItem(Content.ContentLibrary.DisplayName(id));
        _characterSelector.ItemSelected += OnCharacterSelected;
        pickers.AddChild(_characterSelector);
        pickers.AddChild(new Label { Text = "  Move:" });
        _moveSelector = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _moveSelector.ItemSelected += index => SelectMove((int)index);
        pickers.AddChild(_moveSelector);

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddChild(grid);

        grid.AddChild(new Label { Text = "Name hash (hex)" });
        _nameHash = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _nameHash.TextChanged += OnNameHashChanged;
        grid.AddChild(_nameHash);

        foreach (var field in TimingFields)
        {
            var box = NewSpin(field.Min, field.Max, 1);
            box.ValueChanged += v => Edit(m => field.Set(m, (int)v));
            grid.AddChild(new Label { Text = field.Label });
            grid.AddChild(box);
            _intBoxes.Add((field, box));
        }

        foreach (var field in GeometryFields)
        {
            var box = NewSpin(field.Min, field.Max, 0.01);
            box.ValueChanged += v => Edit(m => field.Set(m, (int)System.Math.Round(v * 65536.0)));
            grid.AddChild(new Label { Text = field.Label });
            grid.AddChild(box);
            _unitBoxes.Add((field, box));
        }

        _level = new OptionButton();
        foreach (var level in Enum.GetValues<HitLevel>()) _level.AddItem(level.ToString(), (int)level);
        _level.ItemSelected += i => Edit(m => m with { Level = (HitLevel)(int)i });
        grid.AddChild(new Label { Text = "Hit level" });
        grid.AddChild(_level);

        root.AddChild(new Label { Text = "Traits" });
        var traits = new HFlowContainer();
        root.AddChild(traits);
        foreach (var (label, trait) in TraitBoxes)
        {
            var check = new CheckBox { Text = label };
            check.Toggled += on => Edit(m => m with { Traits = on ? m.Traits | trait : m.Traits & ~trait });
            traits.AddChild(check);
            _traitChecks.Add((trait, check));
        }

        root.AddChild(new Label { Text = "Command: any of these buttons triggers the move" });
        var buttons = new HFlowContainer();
        root.AddChild(buttons);
        foreach (var (label, button) in ButtonBoxes)
        {
            var check = new CheckBox { Text = label };
            check.Toggled += on => Edit(m => SetCommand(m, buttons: on ? MoveCommand.Buttons(m.Command) | button : MoveCommand.Buttons(m.Command) & ~button));
            buttons.AddChild(check);
            _buttonChecks.Add((button, check));
        }

        var commandRow = new HBoxContainer();
        root.AddChild(commandRow);
        commandRow.AddChild(new Label { Text = "Motion:" });
        _motion = new OptionButton();
        foreach (var motion in Enum.GetValues<MotionId>()) _motion.AddItem(motion.ToString(), (int)motion);
        _motion.ItemSelected += i => Edit(m => SetCommand(m, motion: (MotionId)(int)i));
        commandRow.AddChild(_motion);
        commandRow.AddChild(new Label { Text = "  Stance:" });
        _stance = new OptionButton();
        foreach (var stance in Enum.GetValues<MoveStance>()) _stance.AddItem(stance.ToString(), (int)stance);
        _stance.ItemSelected += i => Edit(m => SetCommand(m, stance: (MoveStance)(int)i));
        commandRow.AddChild(_stance);

        root.AddChild(new Label { Text = "Can cancel into (once this move connected)" });
        var cancels = new HFlowContainer();
        root.AddChild(cancels);
        for (int category = 0; category < CancelNames.Length; category++)
        {
            int bit = 1 << category;
            var check = new CheckBox { Text = CancelNames[category] };
            check.Toggled += on => Edit(m => m with { CancelMask = on ? m.CancelMask | bit : m.CancelMask & ~bit });
            cancels.AddChild(check);
            _cancelChecks.Add(check);
        }

        _summary = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        root.AddChild(_summary);

        var actions = new HBoxContainer();
        root.AddChild(actions);
        AddButton(actions, "New move", OnNewMove);
        AddButton(actions, "Delete move", OnDeleteMove);
        AddButton(actions, "Save", OnSave);
        AddButton(actions, "Load", OnLoad);
        AddButton(actions, "Revert to built-in", OnRevert);
        AddButton(actions, "Back", () => GetTree().ChangeSceneToFile(GameSession.MainMenuScene));

        _status = new Label { Text = "Ready", HorizontalAlignment = HorizontalAlignment.Center };
        root.AddChild(_status);
    }

    private static SpinBox NewSpin(double min, double max, double step) => new() { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = SizeFlags.ExpandFill };

    private static void AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private static MoveEntry SetCommand(MoveEntry move, InputButton? buttons = null, MotionId? motion = null, MoveStance? stance = null) =>
        move with
        {
            Command = MoveCommand.Pack(
                buttons ?? MoveCommand.Buttons(move.Command),
                motion ?? MoveCommand.Motion(move.Command),
                stance ?? MoveCommand.Stance(move.Command)),
        };

    // ------------------------------------------------------------------ model <-> widgets

    private void LoadCharacter(string id)
    {
        _character = CharacterRoster.Create(id);
        _document = MoveTableDocument.FromTable(_character.Moves);
    }

    private void RefreshMoveList()
    {
        if (_document == null) return;
        _moveSelector.Clear();
        for (int i = 0; i < _document.Count; i++)
        {
            var move = _document.Get(i);
            _moveSelector.AddItem($"{i}: {MoveLabel(move, i)}  [{MoveCommand.Buttons(move.Command)} {MoveCommand.Motion(move.Command)}]");
        }
        if (_selected >= 0 && _selected < _document.Count) _moveSelector.Select(_selected);
    }

    private static string MoveLabel(MoveEntry move, int index) => index < MoveSlot.Count ? SlotNames[index] : $"extra {move.NameHash:X8}";

    private static readonly string[] SlotNames = ["Stand light", "Stand medium", "Stand heavy", "Crouch light", "Crouch heavy", "Special", "Super"];

    private void SelectMove(int index)
    {
        if (_document == null || index < 0 || index >= _document.Count) return;
        _selected = index;
        var move = _document.Get(index);
        _syncing = true;

        _nameHash.Text = move.NameHash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (field, box) in _intBoxes) box.Value = field.Get(move);
        foreach (var (field, box) in _unitBoxes) box.Value = field.Get(move) / 65536.0;
        _level.Select((int)move.Level);
        foreach (var (trait, box) in _traitChecks) box.ButtonPressed = move.Has(trait);
        var buttons = MoveCommand.Buttons(move.Command);
        foreach (var (button, box) in _buttonChecks) box.ButtonPressed = (buttons & button) != 0;
        _motion.Select((int)MoveCommand.Motion(move.Command));
        _stance.Select((int)MoveCommand.Stance(move.Command));
        for (int i = 0; i < _cancelChecks.Count; i++) _cancelChecks[i].ButtonPressed = (move.CancelMask & (1 << i)) != 0;

        _syncing = false;
        UpdateSummary(move);
    }

    /// <summary>Applies an edit to the selected move; invalid values are reported and the widgets restored.</summary>
    private void Edit(Func<MoveEntry, MoveEntry> change)
    {
        if (_syncing || _document == null || _selected < 0) return;
        try
        {
            var updated = change(_document.Get(_selected));
            _document.Set(_selected, updated);
            _status.Text = "Modified (not saved)";
            UpdateSummary(updated);
        }
        catch (ArgumentOutOfRangeException e)
        {
            _status.Text = $"Invalid value: {e.Message}";
            SelectMove(_selected); // restore the widgets to the stored value
        }
    }

    private void UpdateSummary(MoveEntry move)
    {
        // Both fighters freeze for the hitstop, so only the frames after contact matter.
        int remaining = move.ActiveFrames - 1 + move.RecoveryFrames;
        int onHit = move.EffectiveHitstun - remaining;
        int onBlock = move.EffectiveBlockstun - remaining;
        _summary.Text = $"Total {move.TotalFrames}f  |  startup {move.StartupFrames}  |  on hit {onHit:+#;-#;0}  |  on block {onBlock:+#;-#;0}";
    }

    // ------------------------------------------------------------------ actions

    private void OnCharacterSelected(long index)
    {
        LoadCharacter(CharacterRoster.Ids[(int)index]);
        _selected = 0;
        RefreshMoveList();
        SelectMove(0);
    }

    private void OnNameHashChanged(string value)
    {
        if (_syncing) return;
        if (int.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int hash))
        {
            Edit(m => m with { NameHash = hash });
            RefreshMoveList();
        }
        else
        {
            _status.Text = "Name hash must be hexadecimal (e.g. 1A2B3C4D)";
        }
    }

    private void OnNewMove()
    {
        if (_document == null) return;
        // A new row starts as a copy of the selected move (without a command, so it cannot fire by accident).
        var template = _document.Get(System.Math.Max(0, _selected)) with { Command = 0 };
        _selected = _document.Add(template with { NameHash = template.NameHash ^ _document.Count });
        RefreshMoveList();
        SelectMove(_selected);
        _status.Text = "Move added (assign a command to make it playable)";
    }

    private void OnDeleteMove()
    {
        if (_document == null) return;
        if (!_document.TryRemoveAt(_selected))
        {
            _status.Text = $"Moves 0-{MoveSlot.Count - 1} are canonical slots and cannot be deleted";
            return;
        }
        _selected = System.Math.Min(_selected, _document.Count - 1);
        RefreshMoveList();
        SelectMove(_selected);
        _status.Text = "Move deleted";
    }

    private string CurrentPath => Path.Combine(SaveDirectory, $"{_character?.Id}{CharacterMods.Extension}");

    private void OnSave()
    {
        if (_document == null || _character == null) return;
        try
        {
            Directory.CreateDirectory(SaveDirectory);
            File.WriteAllBytes(CurrentPath, _document.ToBytes());
            _status.Text = $"Saved to {CurrentPath} (restart the fight to play it)";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Error: {e.Message}";
        }
    }

    private void OnLoad()
    {
        if (_character == null) return;
        try
        {
            if (!File.Exists(CurrentPath))
            {
                _status.Text = "No saved table for this character";
                return;
            }
            _document = MoveTableDocument.FromBytes(File.ReadAllBytes(CurrentPath));
            _selected = 0;
            RefreshMoveList();
            SelectMove(0);
            _status.Text = $"Loaded {CurrentPath}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = $"Error: {e.Message}";
        }
    }

    private void OnRevert()
    {
        if (_character == null) return;
        try
        {
            if (File.Exists(CurrentPath)) File.Delete(CurrentPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Error: {e.Message}";
            return;
        }
        CharacterMods.Remove(_character.Id);
        LoadCharacter(_character.Id);
        _selected = 0;
        RefreshMoveList();
        SelectMove(0);
        _status.Text = "Reverted to the built-in table";
    }
}
