using System;
using System.Collections.Generic;
using System.IO;
using Fury.Core.Data;
using Fury.Core.Sim;
using Fury.Core.Authoring;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Visual frame data editor for FMT1 move tables.
/// Allows designers to edit move properties without recompiling code.
/// Changes are exported to binary FMT1 format for hot-reload.
/// </summary>
public partial class FrameDataEditor : Control
{
    private CharacterConfig? _currentCharacter;
    private MoveTableDocument? _document;
    private int _selectedMoveIndex = -1;
    private bool _syncing; // true while SelectMove pushes values into the widgets

    // UI Elements
    private OptionButton? _characterSelector;
    private OptionButton? _moveSelector;
    private LineEdit? _nameHashEdit;
    private SpinBox? _startupSpin;
    private SpinBox? _activeSpin;
    private SpinBox? _recoverySpin;
    private SpinBox? _damageSpin;
    private SpinBox? _hitstopSpin;
    private SpinBox? _pushbackSpin;
    private SpinBox? _reachSpin;
    private SpinBox? _boxHeightSpin;
    private SpinBox? _boxCenterYSpin;
    private Button? _saveButton;
    private Button? _loadButton;
    private Button? _newMoveButton;
    private Button? _deleteMoveButton;
    private Label _statusLabel = null!;
    private Label? _totalFramesLabel;

    // Constants
    private const string DefaultSavePath = "user://characters/";

    // "user://" is virtual; System.IO needs the OS path (see StageEditor).
    private static string SaveDirectory => ProjectSettings.GlobalizePath(DefaultSavePath);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
    }

    public override void _Ready()
    {
        LoadCharacter(CharacterRoster.Ids[0]);
        BuildUI();
        RefreshMoveList();
        SelectMove(0);
    }

    private void LoadCharacter(string id)
    {
        _currentCharacter = CharacterRoster.Create(id);
        _document = MoveTableDocument.FromTable(_currentCharacter.Moves);
    }

    private void BuildUI()
    {
        var mainContainer = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        AddChild(mainContainer);

        // Title
        var title = new Label
        {
            Text = "Fury Engine - Frame Data Editor",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainContainer.AddChild(title);

        // Character selector
        var charContainer = new HBoxContainer();
        mainContainer.AddChild(charContainer);

        charContainer.AddChild(new Label { Text = "Character:" });
        _characterSelector = new OptionButton();
        foreach (var id in CharacterRoster.Ids) _characterSelector.AddItem(id.ToUpperInvariant());
        _characterSelector.ItemSelected += OnCharacterSelected;
        charContainer.AddChild(_characterSelector);

        // Move selector
        var moveContainer = new HBoxContainer();
        mainContainer.AddChild(moveContainer);

        moveContainer.AddChild(new Label { Text = "Move:" });
        _moveSelector = new OptionButton();
        _moveSelector.ItemSelected += OnMoveSelected;
        moveContainer.AddChild(_moveSelector);

        // Properties panel
        var propertiesPanel = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        mainContainer.AddChild(propertiesPanel);

        // Name Hash
        propertiesPanel.AddChild(new Label { Text = "Name Hash:" });
        _nameHashEdit = new LineEdit();
        _nameHashEdit.TextChanged += OnNameHashChanged;
        propertiesPanel.AddChild(_nameHashEdit);

        // Startup frames
        propertiesPanel.AddChild(new Label { Text = "Startup Frames:" });
        _startupSpin = CreateSpinBox(0, 100);
        _startupSpin.ValueChanged += OnStartupChanged;
        propertiesPanel.AddChild(_startupSpin);

        // Active frames
        propertiesPanel.AddChild(new Label { Text = "Active Frames:" });
        _activeSpin = CreateSpinBox(0, 100);
        _activeSpin.ValueChanged += OnActiveChanged;
        propertiesPanel.AddChild(_activeSpin);

        // Recovery frames
        propertiesPanel.AddChild(new Label { Text = "Recovery Frames:" });
        _recoverySpin = CreateSpinBox(0, 100);
        _recoverySpin.ValueChanged += OnRecoveryChanged;
        propertiesPanel.AddChild(_recoverySpin);

        // Damage
        propertiesPanel.AddChild(new Label { Text = "Damage:" });
        _damageSpin = CreateSpinBox(0, 1000);
        _damageSpin.ValueChanged += OnDamageChanged;
        propertiesPanel.AddChild(_damageSpin);

        // Hitstop
        propertiesPanel.AddChild(new Label { Text = "Hitstop Frames:" });
        _hitstopSpin = CreateSpinBox(0, 50);
        _hitstopSpin.ValueChanged += OnHitstopChanged;
        propertiesPanel.AddChild(_hitstopSpin);

        // Pushback
        propertiesPanel.AddChild(new Label { Text = "Pushback (Q16.16):" });
        _pushbackSpin = CreateSpinBox(-100000, 100000);
        _pushbackSpin.ValueChanged += OnPushbackChanged;
        propertiesPanel.AddChild(_pushbackSpin);

        // Reach
        propertiesPanel.AddChild(new Label { Text = "Reach (Q16.16):" });
        _reachSpin = CreateSpinBox(0, 100000);
        _reachSpin.ValueChanged += OnReachChanged;
        propertiesPanel.AddChild(_reachSpin);

        // Box height
        propertiesPanel.AddChild(new Label { Text = "Box Height (Q16.16):" });
        _boxHeightSpin = CreateSpinBox(0, 100000);
        _boxHeightSpin.ValueChanged += OnBoxHeightChanged;
        propertiesPanel.AddChild(_boxHeightSpin);

        // Box center Y
        propertiesPanel.AddChild(new Label { Text = "Box Center Y (Q16.16):" });
        _boxCenterYSpin = CreateSpinBox(-100000, 100000);
        _boxCenterYSpin.ValueChanged += OnBoxCenterYChanged;
        propertiesPanel.AddChild(_boxCenterYSpin);

        // Total frames display
        _totalFramesLabel = new Label
        {
            Text = "Total Frames: 0",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainContainer.AddChild(_totalFramesLabel);

        // Buttons
        var buttonContainer = new HBoxContainer();
        mainContainer.AddChild(buttonContainer);

        _newMoveButton = new Button { Text = "New Move" };
        _newMoveButton.Pressed += OnNewMovePressed;
        buttonContainer.AddChild(_newMoveButton);

        _deleteMoveButton = new Button { Text = "Delete Move" };
        _deleteMoveButton.Pressed += OnDeleteMovePressed;
        buttonContainer.AddChild(_deleteMoveButton);

        _saveButton = new Button { Text = "Save" };
        _saveButton.Pressed += OnSavePressed;
        buttonContainer.AddChild(_saveButton);

        _loadButton = new Button { Text = "Load" };
        _loadButton.Pressed += OnLoadPressed;
        buttonContainer.AddChild(_loadButton);

        // Status
        _statusLabel = new Label
        {
            Text = "Ready",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainContainer.AddChild(_statusLabel);
    }

    private SpinBox CreateSpinBox(int min, int max)
    {
        return new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Value = 0
        };
    }

    private void RefreshMoveList()
    {
        if (_moveSelector == null || _document == null) return;
        _moveSelector.Clear();
        for (int i = 0; i < _document.Count; i++)
        {
            _moveSelector.AddItem($"Move {i}: {_document.Get(i).NameHash:X8}");
        }
        if (_selectedMoveIndex >= 0 && _selectedMoveIndex < _document.Count) _moveSelector.Select(_selectedMoveIndex);
    }

    private void SelectMove(int index)
    {
        if (_document == null) return;
        if (index < 0 || index >= _document.Count) return;

        _selectedMoveIndex = index;
        var move = _document.Get(index);
        _syncing = true;

        if (_nameHashEdit != null) _nameHashEdit.Text = move.NameHash.ToString("X8");
        if (_startupSpin != null) _startupSpin.Value = move.StartupFrames;
        if (_activeSpin != null) _activeSpin.Value = move.ActiveFrames;
        if (_recoverySpin != null) _recoverySpin.Value = move.RecoveryFrames;
        if (_damageSpin != null) _damageSpin.Value = move.Damage;
        if (_hitstopSpin != null) _hitstopSpin.Value = move.HitstopFrames;
        if (_pushbackSpin != null) _pushbackSpin.Value = move.PushbackRaw;
        if (_reachSpin != null) _reachSpin.Value = move.ReachRaw;
        if (_boxHeightSpin != null) _boxHeightSpin.Value = move.BoxHalfHeightRaw;
        if (_boxCenterYSpin != null) _boxCenterYSpin.Value = move.BoxCenterYRaw;
        _syncing = false;

        UpdateTotalFrames();
    }

    /// <summary>Applies an edit to the selected move. All handlers below used to be empty
    /// stubs ("// Update move data"): nothing the designer typed was ever stored.</summary>
    private void Edit(Func<MoveEntry, MoveEntry> change)
    {
        if (_syncing || _document == null || _selectedMoveIndex < 0) return;
        try
        {
            _document.Set(_selectedMoveIndex, change(_document.Get(_selectedMoveIndex)));
            _statusLabel.Text = "Modified (not saved)";
        }
        catch (ArgumentOutOfRangeException e)
        {
            _statusLabel.Text = $"Invalid value: {e.Message}";
            SelectMove(_selectedMoveIndex); // restore widgets to the stored value
        }
        UpdateTotalFrames();
    }

    private void UpdateTotalFrames()
    {
        if (_startupSpin == null || _activeSpin == null || _recoverySpin == null || _totalFramesLabel == null) return;
        int total = (int)(_startupSpin.Value + _activeSpin.Value + _recoverySpin.Value);
        _totalFramesLabel.Text = $"Total Frames: {total}";
    }

    private void OnCharacterSelected(long index)
    {
        LoadCharacter(CharacterRoster.Ids[(int)index]);
        _selectedMoveIndex = 0;
        RefreshMoveList();
        SelectMove(0);
    }

    private void OnMoveSelected(long index)
    {
        SelectMove((int)index);
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
            _statusLabel.Text = "Name hash must be hexadecimal (e.g. 1A2B3C4D)";
        }
    }

    private void OnStartupChanged(double value) => Edit(m => m with { StartupFrames = (int)value });

    private void OnActiveChanged(double value) => Edit(m => m with { ActiveFrames = (int)value });

    private void OnRecoveryChanged(double value) => Edit(m => m with { RecoveryFrames = (int)value });

    private void OnDamageChanged(double value) => Edit(m => m with { Damage = (int)value });

    private void OnHitstopChanged(double value) => Edit(m => m with { HitstopFrames = (int)value });

    private void OnPushbackChanged(double value) => Edit(m => m with { PushbackRaw = (int)value });

    private void OnReachChanged(double value) => Edit(m => m with { ReachRaw = (int)value });

    private void OnBoxHeightChanged(double value) => Edit(m => m with { BoxHalfHeightRaw = (int)value });

    private void OnBoxCenterYChanged(double value) => Edit(m => m with { BoxCenterYRaw = (int)value });

    private void OnNewMovePressed()
    {
        if (_document == null) return;
        // New rows start as a copy of the selected move so they are valid from the start.
        var template = _document.Get(System.Math.Max(0, _selectedMoveIndex));
        _selectedMoveIndex = _document.Add(template with { NameHash = template.NameHash ^ _document.Count });
        RefreshMoveList();
        SelectMove(_selectedMoveIndex);
        _statusLabel.Text = "Move added";
    }

    private void OnDeleteMovePressed()
    {
        if (_document == null) return;
        if (!_document.TryRemoveAt(_selectedMoveIndex))
        {
            _statusLabel.Text = $"Moves 0-{MoveSlot.Count - 1} are canonical slots and cannot be deleted";
            return;
        }
        _selectedMoveIndex = System.Math.Min(_selectedMoveIndex, _document.Count - 1);
        RefreshMoveList();
        SelectMove(_selectedMoveIndex);
        _statusLabel.Text = "Move deleted";
    }

    private string CurrentPath => Path.Combine(SaveDirectory, $"{_currentCharacter?.Id}.fmt2");

    private void OnSavePressed()
    {
        if (_document == null || _currentCharacter == null) return;
        try
        {
            Directory.CreateDirectory(SaveDirectory);
            File.WriteAllBytes(CurrentPath, _document.ToBytes());
            _statusLabel.Text = $"Saved to {CurrentPath}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _statusLabel.Text = $"Error: {e.Message}";
        }
    }

    private void OnLoadPressed()
    {
        if (_currentCharacter == null) return;
        try
        {
            if (!File.Exists(CurrentPath))
            {
                _statusLabel.Text = "File not found";
                return;
            }
            _document = MoveTableDocument.FromBytes(File.ReadAllBytes(CurrentPath));
            _selectedMoveIndex = 0;
            RefreshMoveList();
            SelectMove(0);
            _statusLabel.Text = $"Loaded from {CurrentPath}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _statusLabel.Text = $"Error: {e.Message}";
        }
    }
}
