using Godot;

namespace Fury.Game.UI;

/// <summary>Direct-connect lobby: host (fighter slot 1) or join (slot 2) by IP and port.</summary>
public partial class OnlineMenu : Control
{
    private CheckButton _host = null!;
    private LineEdit _address = null!;
    private SpinBox _port = null!;
    private SpinBox _delay = null!;

    public override void _Ready()
    {
        var column = UiKit.CenterColumn(this, 14);
        column.CustomMinimumSize = new Vector2(520, 0);
        UiKit.Title(column, "ONLINE", 48);
        UiKit.Text(column, "Direct connection. Both players enter the other player's address and the same port (forward UDP on the host router).", 16);

        _host = new CheckButton { Text = "I am the host", ButtonPressed = GameSession.NetIsHost };
        column.AddChild(_host);
        _address = new LineEdit { Text = GameSession.NetRemoteHost, PlaceholderText = "opponent IP or host name" };
        UiKit.Row(column, "Opponent address", _address);
        _port = new SpinBox { MinValue = 1024, MaxValue = 65534, Value = GameSession.NetPort };
        UiKit.Row(column, "UDP port", _port);
        _delay = new SpinBox { MinValue = 0, MaxValue = 6, Value = GameSession.NetInputDelay };
        UiKit.Row(column, "Input delay (frames)", _delay);

        var go = UiKit.Button(column, "CHOOSE FIGHTER & CONNECT", Connect);
        UiKit.Button(column, "BACK", () => UiKit.GoTo(this, GameSession.MainMenuScene));
        go.GrabFocus();
    }

    private void Connect()
    {
        string address = _address.Text.Trim();
        if (address.Length == 0) return;
        GameSession.Mode = GameMode.Online;
        GameSession.NetIsHost = _host.ButtonPressed;
        GameSession.NetRemoteHost = address;
        GameSession.NetPort = (int)_port.Value;
        GameSession.NetInputDelay = (int)_delay.Value;
        UiKit.GoTo(this, GameSession.CharacterSelectScene);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) UiKit.GoTo(this, GameSession.MainMenuScene);
    }
}
