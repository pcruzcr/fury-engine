using Fury.Core.Data;
using Godot;

namespace Fury.Game.UI;

/// <summary>
/// Character selection. Versus asks both players; Arcade, Training and Online only ask the local player
/// (the opponent is the CPU / dummy / remote peer). Fully navigable with keyboard or gamepad focus.
/// </summary>
public partial class CharacterSelect : Control
{
    private int _picking; // 0 = P1, 1 = P2
    private Label _prompt = null!;
    private Button[] _buttons = [];

    public override void _Ready()
    {
        var column = UiKit.CenterColumn(this, 18);
        UiKit.Title(column, "SELECT YOUR FIGHTER", 48);
        _prompt = UiKit.Text(column, "", 24);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        column.AddChild(row);

        var ids = CharacterRoster.Ids;
        _buttons = new Button[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            _buttons[i] = new Button { Text = id.ToUpperInvariant(), CustomMinimumSize = new Vector2(180, 120) };
            _buttons[i].AddThemeFontSizeOverride("font_size", 30);
            _buttons[i].Pressed += () => Pick(id);
            row.AddChild(_buttons[i]);
        }

        UiKit.Button(column, "BACK", Back);
        RefreshPrompt();
        _buttons[0].GrabFocus();
    }

    private static bool NeedsSecondPick => GameSession.Mode == GameMode.Versus;

    private void RefreshPrompt() => _prompt.Text = NeedsSecondPick ? $"PLAYER {_picking + 1} - CHOOSE" : "CHOOSE YOUR CHARACTER";

    private void Pick(string id)
    {
        if (_picking == 0)
        {
            GameSession.P1CharacterId = id;
            if (NeedsSecondPick)
            {
                _picking = 1;
                RefreshPrompt();
                return;
            }

            // The opponent is not a human on this machine: default to the next roster entry (CPU / dummy).
            var ids = CharacterRoster.Ids;
            int index = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) index = i;
            }
            GameSession.P2CharacterId = ids[(index + 1) % ids.Count];
        }
        else
        {
            GameSession.P2CharacterId = id;
        }
        UiKit.GoTo(this, GameSession.FightScene);
    }

    private void Back()
    {
        if (_picking == 1)
        {
            _picking = 0;
            RefreshPrompt();
            return;
        }
        UiKit.GoTo(this, GameSession.MainMenuScene);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) Back();
    }
}
