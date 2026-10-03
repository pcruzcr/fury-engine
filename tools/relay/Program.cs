using System.Net;
using Fury.Core.Net;

// Fury relay server: pairs two players that name the same room and forwards their (end-to-end authenticated) datagrams.
// Usage: dotnet run --project tools/relay -c Release -- [port=7777] [bind-address=0.0.0.0]
int port = 7777;
IPAddress bind = IPAddress.Any;
if (args.Length > 0 && (!int.TryParse(args[0], out port) || port is < 1 or > 65535))
{
    Console.Error.WriteLine("Usage: Fury.Relay [port] [bind-address]");
    return 2;
}

if (args.Length > 1 && !IPAddress.TryParse(args[1], out bind!))
{
    Console.Error.WriteLine($"'{args[1]}' is not an IP address.");
    return 2;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

using var server = new RelayServer(port, bindAddress: bind);
Console.WriteLine($"Fury relay listening on {bind}:{server.Port} (Ctrl+C to stop)");

var report = new Thread(() =>
{
    while (!cancel.IsCancellationRequested)
    {
        if (cancel.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30))) break;
        Console.WriteLine($"{DateTime.UtcNow:u} rooms={server.ActiveRooms} forwarded={server.Forwarded} dropped={server.Dropped}");
    }
}) { IsBackground = true };
report.Start();

server.Run(cancel.Token);
Console.WriteLine("Fury relay stopped.");
return 0;
