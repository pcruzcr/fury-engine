using Fury.Core.Net;
using Godot;

namespace Fury.Game.UI;

/// <summary>
/// Online lobby: connect directly by IP and port, or through a relay server with a shared room code when the players
/// are behind NAT. The host is fighter slot 1, the guest slot 2.
/// </summary>
public partial class OnlineMenu : Control
{
    private CheckButton _host = null!;
    private CheckButton _relay = null!;
    private LineEdit _address = null!;
    private SpinBox _port = null!;
    private LineEdit _relayHost = null!;
    private SpinBox _relayPort = null!;
    private LineEdit _roomCode = null!;
    private Label _status = null!;
    private Control _directRows = null!;
    private Control _relayRows = null!;
    private SpinBox _delay = null!;

    public override void _Ready()
    {
        var column = UiKit.CenterColumn(this, 12);
        column.CustomMinimumSize = new Vector2(560, 0);
        UiKit.Title(column, "ONLINE", 48);
        UiKit.Text(column, "One player hosts, the other joins. Direct needs the host's UDP port forwarded; a relay needs nothing.", 16);

        _host = new CheckButton { Text = "I am the host", ButtonPressed = GameSession.NetIsHost };
        column.AddChild(_host);
        _relay = new CheckButton { Text = "Use a relay server (no port forwarding)", ButtonPressed = GameSession.NetUseRelay };
        _relay.Toggled += _ => RefreshRows();
        column.AddChild(_relay);

        var direct = new VBoxContainer();
        _directRows = direct;
        column.AddChild(direct);
        _address = new LineEdit { Text = GameSession.NetRemoteHost, PlaceholderText = "opponent IP or host name" };
        UiKit.Row(direct, "Opponent address", _address);
        _port = new SpinBox { MinValue = 1024, MaxValue = 65534, Value = GameSession.NetPort };
        UiKit.Row(direct, "UDP port", _port);

        var relay = new VBoxContainer();
        _relayRows = relay;
        column.AddChild(relay);
        _relayHost = new LineEdit { Text = GameSession.NetRelayHost, PlaceholderText = "relay address" };
        UiKit.Row(relay, "Relay address", _relayHost);
        _relayPort = new SpinBox { MinValue = 1024, MaxValue = 65534, Value = GameSession.NetRelayPort };
        UiKit.Row(relay, "Relay UDP port", _relayPort);
        _roomCode = new LineEdit { Text = GameSession.NetRoomCode, PlaceholderText = "XXXXX-XXXXX (same on both sides)" };
        var codeRow = UiKit.Row(relay, "Room code", _roomCode);
        var generate = new Button { Text = "NEW" };
        generate.Pressed += () => _roomCode.Text = RelayRoom.GenerateCode();
        codeRow.AddChild(generate);
        UiKit.Text(relay, "Share the code with your opponent privately: it is the secret that keeps the relay out of your match.", 14);

        _delay = new SpinBox { MinValue = 0, MaxValue = 6, Value = GameSession.NetInputDelay };
        UiKit.Row(column, "Input delay (frames)", _delay);

        _status = UiKit.Text(column, "", 16);
        var go = UiKit.Button(column, "CHOOSE FIGHTER & CONNECT", Connect);
        UiKit.Button(column, "BACK", () => UiKit.GoTo(this, GameSession.MainMenuScene));
        go.GrabFocus();
        RefreshRows();
    }

    private void RefreshRows()
    {
        _directRows.Visible = !_relay.ButtonPressed;
        _relayRows.Visible = _relay.ButtonPressed;
    }

    private void Connect()
    {
        bool relay = _relay.ButtonPressed;
        string address = (relay ? _relayHost.Text : _address.Text).Trim();
        if (address.Length == 0)
        {
            _status.Text = relay ? "Enter the relay address." : "Enter the opponent's address.";
            return;
        }

        if (relay && RelayRoom.Normalize(_roomCode.Text).Length < RelayRoom.MinCodeLength)
        {
            _status.Text = Content.Localization.F("The room code needs at least {0} characters (press NEW to generate one).", RelayRoom.MinCodeLength);
            return;
        }

        GameSession.Mode = GameMode.Online;
        GameSession.NetIsHost = _host.ButtonPressed;
        GameSession.NetUseRelay = relay;
        if (relay)
        {
            GameSession.NetRelayHost = address;
            GameSession.NetRelayPort = (int)_relayPort.Value;
            GameSession.NetRoomCode = _roomCode.Text.Trim();
        }
        else
        {
            GameSession.NetRemoteHost = address;
            GameSession.NetPort = (int)_port.Value;
        }

        GameSession.NetInputDelay = (int)_delay.Value;
        UiKit.GoTo(this, GameSession.CharacterSelectScene);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel")) UiKit.GoTo(this, GameSession.MainMenuScene);
    }
}
