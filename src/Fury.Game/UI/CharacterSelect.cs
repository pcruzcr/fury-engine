using Fury.Core.Data;
using Fury.Game.Content;
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

        // A grid, so a roster of any size stays on screen; each cell shows the pack's portrait when it has one.
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 16);
        var rosterCenter = new CenterContainer();
        column.AddChild(rosterCenter);
        rosterCenter.AddChild(grid);

        var entries = ContentLibrary.Characters;
        _buttons = new Button[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            _buttons[i] = BuildCard(entry);
            _buttons[i].Pressed += () => Pick(entry.Id);
            grid.AddChild(_buttons[i]);
        }

        UiKit.Button(column, "BACK", Back);
        RefreshPrompt();
        _buttons[0].GrabFocus();
    }

    private static Button BuildCard(CharacterEntry entry)
    {
        var button = new Button
        {
            Text = entry.DisplayName,
            CustomMinimumSize = new Vector2(180, 150),
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            VerticalIconAlignment = VerticalAlignment.Top,
        };
        button.AddThemeFontSizeOverride("font_size", 26);
        button.AddThemeColorOverride("font_color", entry.Accent);

        string? portrait = entry.PortraitPath;
        var texture = portrait == null ? null : AssetLoader.LoadTexture(portrait);
        if (texture != null)
        {
            button.Icon = texture;
            button.CustomMinimumSize = new Vector2(180, 230);
        }
        return button;
    }

    private static bool NeedsSecondPick => GameSession.Mode == GameMode.Versus;

    private void RefreshPrompt() => _prompt.Text = NeedsSecondPick ? Content.Localization.F("PLAYER {0} - CHOOSE", _picking + 1) : Content.Localization.T("CHOOSE YOUR CHARACTER");

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
                if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase)) index = i;
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
