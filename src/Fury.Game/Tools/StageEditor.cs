using Fury.Core.Authoring;
using Fury.Game.Stages;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Stage editor: arena size, gravity and background, plus platforms (solid, pass-through, bounce, conveyor, moving) and
/// hazards, every property editable with a live scaled preview. Stages are saved to <c>user://stages/</c> and validated on save;
/// "Play" selects the stage and starts a versus match on it. Document coordinates: pixels, origin at the centre of the floor,
/// Y up (see <see cref="StageData"/>).
/// </summary>
public partial class StageEditor : Control
{
    private StageData _stage = NewStage();
    private int _selected = -1; // platforms first, then hazards
    private bool _syncing;

    private OptionButton _stageSelector = null!;
    private LineEdit _name = null!;
    private SpinBox _width = null!, _height = null!, _gravity = null!;
    private ColorPickerButton _background = null!;
    private OptionButton _elements = null!;
    private SpinBox _x = null!, _y = null!, _w = null!, _h = null!, _strength = null!, _moveX = null!, _moveY = null!, _moveFrames = null!, _damage = null!;
    private OptionButton _kind = null!;
    private Control _platformOnly = null!;
    private Control _hazardOnly = null!;
    private StagePreview _preview = null!;
    private Label _status = null!;

    private static string SaveDirectory => ProjectSettings.GlobalizePath(StageLoader.Folder);

    private static StageData NewStage() => new()
    {
        Name = "New Stage",
        Width = 1920,
        Height = 1080,
        Gravity = 1.0f,
        BackgroundColor = new ColorRgba(0.1f, 0.1f, 0.2f),
    };

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
    }

    public override void _Ready()
    {
        BuildUi();
        RefreshStageList();
        SyncAll();
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var back = new ColorRect { Color = new Color(0.07f, 0.07f, 0.12f), MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(back);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scroll);
        var root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(root);

        root.AddChild(new Label { Text = "Fury Engine - Stage Editor", HorizontalAlignment = HorizontalAlignment.Center });

        var pick = new HBoxContainer();
        root.AddChild(pick);
        pick.AddChild(new Label { Text = "Stage:" });
        _stageSelector = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _stageSelector.ItemSelected += OnStageSelected;
        pick.AddChild(_stageSelector);

        _preview = new StagePreview();
        root.AddChild(_preview);

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddChild(grid);
        _name = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _name.TextChanged += t => EditStage(() => _stage.Name = t);
        AddRow(grid, "Name", _name);
        _width = Spin(480, 20000, 10, v => EditStage(() => _stage.Width = (int)v));
        AddRow(grid, "Arena width (px)", _width);
        _height = Spin(100, 20000, 10, v => EditStage(() => _stage.Height = (int)v));
        AddRow(grid, "Arena height (px)", _height);
        _gravity = Spin(0.1, 5, 0.05, v => EditStage(() => _stage.Gravity = (float)v));
        AddRow(grid, "Gravity (x)", _gravity);
        _background = new ColorPickerButton { CustomMinimumSize = new Vector2(0, 30), SizeFlagsHorizontal = SizeFlags.ExpandFill, EditAlpha = false };
        _background.ColorChanged += c => EditStage(() => _stage.BackgroundColor = new ColorRgba(c.R, c.G, c.B, 1f));
        AddRow(grid, "Background", _background);

        root.AddChild(new Label { Text = "Elements (at most 8 platforms + hazards)" });
        _elements = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _elements.ItemSelected += i =>
        {
            _selected = (int)i;
            SyncElement();
        };
        root.AddChild(_elements);

        var elementButtons = new HBoxContainer();
        root.AddChild(elementButtons);
        AddButton(elementButtons, "Add platform", OnAddPlatform);
        AddButton(elementButtons, "Add hazard", OnAddHazard);
        AddButton(elementButtons, "Delete element", OnDeleteElement);

        var props = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddChild(props);
        _x = Spin(-20000, 20000, 5, v => EditElement(p => p.X = (int)v, h => h.X = (int)v));
        AddRow(props, "Centre X (px)", _x);
        _y = Spin(-20000, 20000, 5, v => EditElement(p => p.Y = (int)v, h => h.Y = (int)v));
        AddRow(props, "Centre Y (px, up)", _y);
        _w = Spin(4, 20000, 5, v => EditElement(p => p.Width = (int)v, h => h.Width = (int)v));
        AddRow(props, "Width (px)", _w);
        _h = Spin(4, 2000, 2, v => EditElement(p => p.Height = (int)v, h => h.Height = (int)v));
        AddRow(props, "Height (px)", _h);

        var platformProps = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _platformOnly = platformProps;
        root.AddChild(platformProps);
        _kind = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var kind in Enum.GetValues<PlatformKind>()) _kind.AddItem(kind.ToString(), (int)kind);
        _kind.ItemSelected += i => EditElement(p => p.Kind = (PlatformKind)(int)i, null);
        AddRow(platformProps, "Kind", _kind);
        _strength = Spin(-100000, 100000, 5, v => EditElement(p => p.Strength = (int)v, null));
        AddRow(platformProps, "Strength (0 = default)", _strength);
        _moveX = Spin(-20000, 20000, 5, v => EditElement(p => p.MoveToX = (int)v, null));
        AddRow(platformProps, "Moves to X (px)", _moveX);
        _moveY = Spin(-20000, 20000, 5, v => EditElement(p => p.MoveToY = (int)v, null));
        AddRow(platformProps, "Moves to Y (px)", _moveY);
        _moveFrames = Spin(0, 36000, 1, v => EditElement(p => p.MoveFrames = (int)v, null));
        AddRow(platformProps, "Move period (frames, 0 = static)", _moveFrames);

        var hazardProps = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _hazardOnly = hazardProps;
        root.AddChild(hazardProps);
        _damage = Spin(0, 500, 1, v => EditElement(null, h => h.Damage = (int)v));
        AddRow(hazardProps, "Damage per half second", _damage);

        var actions = new HBoxContainer();
        root.AddChild(actions);
        AddButton(actions, "New", OnNew);
        AddButton(actions, "Save", OnSave);
        AddButton(actions, "Play on this stage", OnPlay);
        AddButton(actions, "Back", () => GetTree().ChangeSceneToFile(GameSession.MainMenuScene));

        _status = new Label { Text = "Ready", HorizontalAlignment = HorizontalAlignment.Center };
        root.AddChild(_status);
    }

    private static SpinBox Spin(double min, double max, double step, Action<double> onChanged)
    {
        var box = new SpinBox { MinValue = min, MaxValue = max, Step = step, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.ValueChanged += v => onChanged(v);
        return box;
    }

    private static void AddRow(Container grid, string label, Control control)
    {
        grid.AddChild(new Label { Text = label });
        grid.AddChild(control);
    }

    private static void AddButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }

    // ------------------------------------------------------------------ model <-> widgets

    private int ElementCount => _stage.Platforms.Count + _stage.Hazards.Count;

    private void EditStage(Action change)
    {
        if (_syncing) return;
        change();
        _status.Text = "Modified (not saved)";
        _preview.Show(_stage, _selected);
    }

    private void EditElement(Action<PlatformData>? platform, Action<HazardData>? hazard)
    {
        if (_syncing || _selected < 0 || _selected >= ElementCount) return;
        if (_selected < _stage.Platforms.Count) platform?.Invoke(_stage.Platforms[_selected]);
        else hazard?.Invoke(_stage.Hazards[_selected - _stage.Platforms.Count]);
        _status.Text = "Modified (not saved)";
        _preview.Show(_stage, _selected);
    }

    private void SyncAll()
    {
        _syncing = true;
        _name.Text = _stage.Name;
        _width.Value = _stage.Width;
        _height.Value = _stage.Height;
        _gravity.Value = _stage.Gravity;
        _background.Color = new Color(_stage.BackgroundColor.R, _stage.BackgroundColor.G, _stage.BackgroundColor.B, 1f);
        _syncing = false;
        RefreshElementList();
        SyncElement();
    }

    private void RefreshElementList()
    {
        _elements.Clear();
        for (int i = 0; i < _stage.Platforms.Count; i++) _elements.AddItem($"Platform {i + 1} [{_stage.Platforms[i].Kind}]");
        for (int i = 0; i < _stage.Hazards.Count; i++) _elements.AddItem($"Hazard {i + 1}");
        if (_selected >= ElementCount) _selected = ElementCount - 1;
        if (_selected >= 0) _elements.Select(_selected);
    }

    private void SyncElement()
    {
        bool has = _selected >= 0 && _selected < ElementCount;
        bool isPlatform = has && _selected < _stage.Platforms.Count;
        _platformOnly.Visible = isPlatform;
        _hazardOnly.Visible = has && !isPlatform;

        _syncing = true;
        if (isPlatform)
        {
            var p = _stage.Platforms[_selected];
            _x.Value = p.X;
            _y.Value = p.Y;
            _w.Value = p.Width;
            _h.Value = p.Height;
            _kind.Select((int)p.Kind);
            _strength.Value = p.Strength;
            _moveX.Value = p.MoveToX;
            _moveY.Value = p.MoveToY;
            _moveFrames.Value = p.MoveFrames;
        }
        else if (has)
        {
            var h = _stage.Hazards[_selected - _stage.Platforms.Count];
            _x.Value = h.X;
            _y.Value = h.Y;
            _w.Value = h.Width;
            _h.Value = h.Height;
            _damage.Value = h.Damage;
        }
        _syncing = false;
        _preview.Show(_stage, _selected);
    }

    // ------------------------------------------------------------------ actions

    private bool CanAdd()
    {
        if (ElementCount < StageData.MaxElements) return true;
        _status.Text = $"A stage holds at most {StageData.MaxElements} platforms and hazards";
        return false;
    }

    private void OnAddPlatform()
    {
        if (!CanAdd()) return;
        _stage.Platforms.Add(new PlatformData { X = 0, Y = 150, Width = 240, Height = 24 });
        _selected = _stage.Platforms.Count - 1;
        RefreshElementList();
        SyncElement();
        _status.Text = "Platform added";
    }

    private void OnAddHazard()
    {
        if (!CanAdd()) return;
        _stage.Hazards.Add(new HazardData { X = 0, Y = 30, Width = 160, Height = 60, Damage = 10 });
        _selected = ElementCount - 1;
        RefreshElementList();
        SyncElement();
        _status.Text = "Hazard added";
    }

    private void OnDeleteElement()
    {
        if (_selected < 0 || _selected >= ElementCount)
        {
            _status.Text = "No element selected";
            return;
        }
        if (_selected < _stage.Platforms.Count) _stage.Platforms.RemoveAt(_selected);
        else _stage.Hazards.RemoveAt(_selected - _stage.Platforms.Count);
        _selected = Math.Min(_selected, ElementCount - 1);
        RefreshElementList();
        SyncElement();
        _status.Text = "Element deleted";
    }

    private void OnNew()
    {
        _stage = NewStage();
        _selected = -1;
        SyncAll();
        RefreshStageList();
        _status.Text = "New stage";
    }

    private bool TrySave(out string path)
    {
        path = Path.Combine(SaveDirectory, StageSerializer.SafeFileName(_stage.Name));
        try
        {
            _stage.Validate();
            Directory.CreateDirectory(SaveDirectory);
            File.WriteAllText(path, StageSerializer.ToJson(_stage));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = $"Cannot save: {e.Message}";
            return false;
        }
    }

    private void OnSave()
    {
        if (!TrySave(out string path)) return;
        _status.Text = $"Saved to {path}";
        RefreshStageList();
    }

    private void OnPlay()
    {
        if (!TrySave(out _)) return;
        GameSettings.StageName = Path.GetFileNameWithoutExtension(StageSerializer.SafeFileName(_stage.Name));
        GameSettings.Save();
        GameSession.Mode = GameMode.Versus;
        GetTree().ChangeSceneToFile(GameSession.FightScene);
    }

    private void RefreshStageList()
    {
        _stageSelector.Clear();
        _stageSelector.AddItem("(editing) " + _stage.Name);
        foreach (string name in StageLoader.List()) _stageSelector.AddItem(name);
    }

    private void OnStageSelected(long index)
    {
        if (index <= 0) return;
        var loaded = StageLoader.TryLoad(_stageSelector.GetItemText((int)index));
        if (loaded == null)
        {
            _status.Text = "That stage file is missing or invalid";
            return;
        }
        _stage = loaded.Data;
        _selected = ElementCount > 0 ? 0 : -1;
        SyncAll();
        _status.Text = $"Loaded {loaded.Data.Name}";
    }
}
