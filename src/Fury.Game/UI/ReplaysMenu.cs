using Godot;

namespace Fury.Game.UI;

/// <summary>Lists the replays saved in <c>user://replays</c> and plays or deletes the selected one.</summary>
public partial class ReplaysMenu : Control
{
    private ItemList _list = null!;
    private Label _status = null!;
    private string[] _files = [];

    private static string ReplayDirectory => ProjectSettings.GlobalizePath("user://replays");

    public override void _Ready()
    {
        var column = UiKit.CenterColumn(this, 12);
        column.CustomMinimumSize = new Vector2(620, 0);
        UiKit.Title(column, "REPLAYS", 48);

        _list = new ItemList { CustomMinimumSize = new Vector2(0, 260), SelectMode = ItemList.SelectModeEnum.Single };
        _list.ItemActivated += _ => Play();
        column.AddChild(_list);
        _status = UiKit.Text(column, "", 16);

        var play = UiKit.Button(column, "PLAY", Play);
        UiKit.Button(column, "DELETE", Delete, 20);
        UiKit.Button(column, "BACK", () => UiKit.GoTo(this, GameSession.MainMenuScene));
        Refresh();
        play.GrabFocus();
    }

    private void Refresh()
    {
        _list.Clear();
        _files = Directory.Exists(ReplayDirectory)
            ? Directory.GetFiles(ReplayDirectory, "*.fry").OrderByDescending(f => f, StringComparer.Ordinal).ToArray()
            : [];
        foreach (var file in _files) _list.AddItem(Path.GetFileNameWithoutExtension(file));
        _status.Text = _files.Length == 0 ? "No replays yet: finish an Arcade or Versus round." : "";
        if (_files.Length > 0) _list.Select(0);
    }

    private string? Selected()
    {
        var picked = _list.GetSelectedItems();
        return picked.Length == 1 && picked[0] < _files.Length ? _files[picked[0]] : null;
    }

    private void Play()
    {
        var file = Selected();
        if (file == null) return;
        GameSession.Mode = GameMode.Replay;
        GameSession.ReplayPath = file;
        UiKit.GoTo(this, GameSession.FightScene);
    }

    private void Delete()
    {
        var file = Selected();
        if (file == null) return;
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Cannot delete: {e.Message}";
            return;
        }
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) UiKit.GoTo(this, GameSession.MainMenuScene);
    }
}
