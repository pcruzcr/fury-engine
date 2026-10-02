using System;
using System.Collections.Generic;
using System.IO;
using Fury.Core.Authoring;
using Godot;

namespace Fury.Game.Tools;

/// <summary>
/// Visual stage editor for creating and modifying fight stages.
/// Allows placement of platforms, hazards, and interactive elements.
/// </summary>
public partial class StageEditor : Control
{
    private StageData? _currentStage;
    private int _selectedElementIndex = -1;
    
    // UI Elements
    private OptionButton? _stageSelector;
    private SpinBox? _widthSpin;
    private SpinBox? _heightSpin;
    private SpinBox? _gravitySpin;
    private ColorPicker? _bgColorPicker;
    private Button? _saveButton;
    private Button? _loadButton;
    private Button? _newStageButton;
    private Button? _addPlatformButton;
    private Button? _addHazardButton;
    private Button? _deleteElementButton;
    private Label _statusLabel = null!;
    private Label? _elementInfoLabel;
    
    // Constants
    private const string DefaultSavePath = "user://stages/";

    // Godot "user://" is a virtual path: System.IO needs the OS path. The editor used to
    // pass "user://stages/" straight to Directory.CreateDirectory, which created a literal
    // "user:" folder (or failed) and never saved anything.
    private static string SaveDirectory => ProjectSettings.GlobalizePath(DefaultSavePath);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) GetTree().ChangeSceneToFile(GameSession.MainMenuScene);
    }

    public override void _Ready()
    {
        _currentStage = NewStage();
        BuildUI();
        RefreshStageList();
        UpdateElementInfo();
    }

    private static StageData NewStage() => new()
    {
        Name = "New Stage",
        Width = 1920,
        Height = 1080,
        Gravity = 1.0f,
        BackgroundColor = new ColorRgba(0.1f, 0.1f, 0.2f),
        Platforms = new List<PlatformData>(),
        Hazards = new List<HazardData>()
    };
    
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
            Text = "Fury Engine - Stage Editor",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainContainer.AddChild(title);
        
        // Stage selector
        var selectorContainer = new HBoxContainer();
        mainContainer.AddChild(selectorContainer);
        
        selectorContainer.AddChild(new Label { Text = "Stage:" });
        _stageSelector = new OptionButton();
        _stageSelector.ItemSelected += OnStageSelected;
        selectorContainer.AddChild(_stageSelector);
        
        // Properties panel
        var propertiesPanel = new GridContainer
        {
            Columns = 2
        };
        mainContainer.AddChild(propertiesPanel);
        
        // Width
        propertiesPanel.AddChild(new Label { Text = "Width:" });
        _widthSpin = CreateSpinBox(100, 10000, 1920);
        _widthSpin.ValueChanged += OnWidthChanged;
        propertiesPanel.AddChild(_widthSpin);
        
        // Height
        propertiesPanel.AddChild(new Label { Text = "Height:" });
        _heightSpin = CreateSpinBox(100, 10000, 1080);
        _heightSpin.ValueChanged += OnHeightChanged;
        propertiesPanel.AddChild(_heightSpin);
        
        // Gravity
        propertiesPanel.AddChild(new Label { Text = "Gravity:" });
        _gravitySpin = CreateSpinBox(0, 5, 1);
        _gravitySpin.ValueChanged += OnGravityChanged;
        propertiesPanel.AddChild(_gravitySpin);
        
        // Background color
        propertiesPanel.AddChild(new Label { Text = "Background:" });
        _bgColorPicker = new ColorPicker();
        _bgColorPicker.ColorChanged += OnBgColorChanged;
        propertiesPanel.AddChild(_bgColorPicker);
        
        // Element info
        _elementInfoLabel = new Label
        {
            Text = "No element selected",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainContainer.AddChild(_elementInfoLabel);
        
        // Buttons
        var buttonContainer = new HBoxContainer();
        mainContainer.AddChild(buttonContainer);
        
        _newStageButton = new Button { Text = "New Stage" };
        _newStageButton.Pressed += OnNewStagePressed;
        buttonContainer.AddChild(_newStageButton);
        
        _addPlatformButton = new Button { Text = "Add Platform" };
        _addPlatformButton.Pressed += OnAddPlatformPressed;
        buttonContainer.AddChild(_addPlatformButton);
        
        _addHazardButton = new Button { Text = "Add Hazard" };
        _addHazardButton.Pressed += OnAddHazardPressed;
        buttonContainer.AddChild(_addHazardButton);
        
        _deleteElementButton = new Button { Text = "Delete Element" };
        _deleteElementButton.Pressed += OnDeleteElementPressed;
        buttonContainer.AddChild(_deleteElementButton);
        
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
    
    private SpinBox CreateSpinBox(double min, double max, double value)
    {
        return new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Value = value
        };
    }
    
    private void RefreshStageList()
    {
        if (_stageSelector == null || _currentStage == null) return;
        _stageSelector.Clear();
        _stageSelector.AddItem(_currentStage.Name);
        if (!Directory.Exists(SaveDirectory)) return;
        foreach (var file in Directory.GetFiles(SaveDirectory, "*" + StageSerializer.Extension))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name != Path.GetFileNameWithoutExtension(StageSerializer.SafeFileName(_currentStage.Name)))
            {
                _stageSelector.AddItem(name);
            }
        }
    }

    private void SyncFieldsFromStage()
    {
        if (_currentStage == null) return;
        _widthSpin?.SetValueNoSignal(_currentStage.Width);
        _heightSpin?.SetValueNoSignal(_currentStage.Height);
        _gravitySpin?.SetValueNoSignal(_currentStage.Gravity);
        if (_bgColorPicker != null) _bgColorPicker.Color = new Color(_currentStage.BackgroundColor.R, _currentStage.BackgroundColor.G, _currentStage.BackgroundColor.B, _currentStage.BackgroundColor.A);
    }

    private int ElementCount => _currentStage == null ? 0 : _currentStage.Platforms.Count + _currentStage.Hazards.Count;

    private void UpdateElementInfo()
    {
        if (_elementInfoLabel == null || _currentStage == null) return;
        if (_selectedElementIndex < 0 || _selectedElementIndex >= ElementCount)
        {
            _selectedElementIndex = -1;
            _elementInfoLabel.Text = "No element selected";
            return;
        }
        int platforms = _currentStage.Platforms.Count;
        _elementInfoLabel.Text = _selectedElementIndex < platforms
            ? $"Platform #{_selectedElementIndex + 1} of {platforms}"
            : $"Hazard #{_selectedElementIndex - platforms + 1} of {_currentStage.Hazards.Count}";
    }

    private void OnStageSelected(long index)
    {
        if (_stageSelector == null || index <= 0) return; // item 0 is the stage being edited
        LoadFromFile(Path.Combine(SaveDirectory, _stageSelector.GetItemText((int)index) + StageSerializer.Extension));
    }

    private void OnWidthChanged(double value)
    {
        if (_currentStage == null) return;
        _currentStage.Width = (int)value;
    }

    private void OnHeightChanged(double value)
    {
        if (_currentStage == null) return;
        _currentStage.Height = (int)value;
    }

    private void OnGravityChanged(double value)
    {
        if (_currentStage == null) return;
        _currentStage.Gravity = (float)value;
    }

    private void OnBgColorChanged(Color color)
    {
        if (_currentStage == null) return;
        _currentStage.BackgroundColor = new ColorRgba(color.R, color.G, color.B, color.A);
    }

    private void OnNewStagePressed()
    {
        _currentStage = NewStage();
        _selectedElementIndex = -1;
        SyncFieldsFromStage();
        RefreshStageList();
        UpdateElementInfo();
        _statusLabel.Text = "New stage created";
    }

    private void OnAddPlatformPressed()
    {
        if (_currentStage == null) return;
        _currentStage.Platforms.Add(new PlatformData { X = 0, Y = 0, Width = 200, Height = 50 });
        _selectedElementIndex = _currentStage.Platforms.Count - 1;
        UpdateElementInfo();
        _statusLabel.Text = "Platform added";
    }

    private void OnAddHazardPressed()
    {
        if (_currentStage == null) return;
        _currentStage.Hazards.Add(new HazardData { X = 0, Y = 0, Width = 100, Height = 100, Damage = 10 });
        _selectedElementIndex = _currentStage.Platforms.Count + _currentStage.Hazards.Count - 1;
        UpdateElementInfo();
        _statusLabel.Text = "Hazard added";
    }

    private void OnDeleteElementPressed()
    {
        // It used to report "Element deleted" without deleting anything.
        if (_currentStage == null || _selectedElementIndex < 0 || _selectedElementIndex >= ElementCount)
        {
            _statusLabel.Text = "No element selected";
            return;
        }
        int platforms = _currentStage.Platforms.Count;
        if (_selectedElementIndex < platforms)
        {
            _currentStage.Platforms.RemoveAt(_selectedElementIndex);
        }
        else
        {
            _currentStage.Hazards.RemoveAt(_selectedElementIndex - platforms);
        }
        _selectedElementIndex = System.Math.Min(_selectedElementIndex, ElementCount - 1);
        UpdateElementInfo();
        _statusLabel.Text = "Element deleted";
    }

    private void OnSavePressed()
    {
        if (_currentStage == null) return;
        try
        {
            Directory.CreateDirectory(SaveDirectory);
            var path = Path.Combine(SaveDirectory, StageSerializer.SafeFileName(_currentStage.Name));
            File.WriteAllText(path, StageSerializer.ToJson(_currentStage));
            _statusLabel.Text = $"Saved to {path}";
            RefreshStageList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _statusLabel.Text = $"Error: {e.Message}";
        }
    }

    private void OnLoadPressed()
    {
        if (_currentStage == null) return;
        LoadFromFile(Path.Combine(SaveDirectory, StageSerializer.SafeFileName(_currentStage.Name)));
    }

    private void LoadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _statusLabel.Text = "File not found";
                return;
            }
            _currentStage = StageSerializer.FromJson(File.ReadAllText(path));
            _selectedElementIndex = -1;
            SyncFieldsFromStage();
            UpdateElementInfo();
            _statusLabel.Text = $"Loaded from {path}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _statusLabel.Text = $"Error: {e.Message}";
        }
    }
}
