using Godot;

namespace Fury.Game.UI;

/// <summary>
/// Controls screen: rebind every fight action of both players on the keyboard and every pad button, then save. Select a
/// button, press the new key / pad button (Esc cancels). A key can only drive one action per player; stealing it unbinds
/// the other action and says so. Bindings persist in <c>user://input.cfg</c> and apply immediately.
/// </summary>
public partial class ControlsMenu : Control
{
    private enum Kind
    {
        Key,
        Pad,
    }

    private sealed record Slot(Kind Kind, int Player, string Suffix, Button Button);

    private readonly List<Slot> _slots = [];
    private Slot? _listening;
    private Label _status = null!;

    public override void _Ready()
    {
        InputBindings.Load();

        var column = UiKit.CenterColumn(this, 10);
        column.CustomMinimumSize = new Vector2(760, 0);
        UiKit.Title(column, "CONTROLS", 44);
        _status = UiKit.Text(column, "Select a button and press the new key. Esc cancels.", 16);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 340), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);

        foreach (string header in new[] { "Action", "Player 1 key", "Player 2 key", "Gamepad" }) grid.AddChild(new Label { Text = header });

        foreach (var (suffix, _, label) in InputBindings.Actions)
        {
            grid.AddChild(new Label { Text = label });
            AddSlot(grid, Kind.Key, 0, suffix);
            AddSlot(grid, Kind.Key, 1, suffix);
            if (suffix is "left" or "right" or "up" or "down") grid.AddChild(new Label { Text = "D-pad / stick", Modulate = new Color(1, 1, 1, 0.6f) });
            else AddSlot(grid, Kind.Pad, 0, suffix);
        }

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddChild(buttons);
        var reset = new Button { Text = "Reset to defaults", CustomMinimumSize = new Vector2(0, 42) };
        reset.Pressed += () =>
        {
            InputBindings.ResetDefaults();
            RefreshAll();
            _status.Text = "Defaults restored.";
        };
        buttons.AddChild(reset);
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(160, 42) };
        back.Pressed += () => UiKit.GoTo(this, GameSession.OptionsScene);
        buttons.AddChild(back);

        RefreshAll();
        back.GrabFocus();
    }

    private void AddSlot(Container grid, Kind kind, int player, string suffix)
    {
        var button = new Button { CustomMinimumSize = new Vector2(150, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var slot = new Slot(kind, player, suffix, button);
        button.Pressed += () => BeginListening(slot);
        grid.AddChild(button);
        _slots.Add(slot);
    }

    private static string Describe(Slot slot)
    {
        if (slot.Kind == Kind.Key)
        {
            var key = InputBindings.GetKey(slot.Player, slot.Suffix);
            return key == Key.None ? "-" : OS.GetKeycodeString(key);
        }
        var pad = InputBindings.PadButton(slot.Suffix);
        return pad == JoyButton.Invalid ? "-" : pad.ToString();
    }

    private void RefreshAll()
    {
        foreach (var slot in _slots) slot.Button.Text = Describe(slot);
    }

    private void BeginListening(Slot slot)
    {
        if (_listening != null) _listening.Button.Text = Describe(_listening);
        _listening = slot;
        slot.Button.Text = slot.Kind == Kind.Key ? "press a key..." : "press a pad button...";
    }

    public override void _Input(InputEvent @event)
    {
        if (_listening == null) return;
        var slot = _listening;

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.PhysicalKeycode == Key.Escape)
            {
                Cancel();
                return;
            }
            if (slot.Kind != Kind.Key) return; // a pad slot only accepts pad buttons
            string? displaced = InputBindings.SetKey(slot.Player, slot.Suffix, key.PhysicalKeycode);
            _status.Text = displaced == null ? "Saved." : $"Saved. That key was used by \"{displaced}\", which is now unbound.";
            Finish();
        }
        else if (@event is InputEventJoypadButton { Pressed: true } pad && slot.Kind == Kind.Pad)
        {
            GetViewport().SetInputAsHandled();
            InputBindings.SetPadButton(slot.Suffix, pad.ButtonIndex);
            _status.Text = "Saved.";
            Finish();
        }
    }

    private void Finish()
    {
        _listening = null;
        RefreshAll();
    }

    private void Cancel()
    {
        _listening = null;
        RefreshAll();
        _status.Text = "Cancelled.";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_listening == null && @event.IsActionPressed("ui_cancel")) UiKit.GoTo(this, GameSession.OptionsScene);
    }
}
