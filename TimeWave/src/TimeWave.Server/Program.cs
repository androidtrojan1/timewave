using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using TimeWave.Core;
using TimeWave.Server;
using TimeWave.Shared;

// TimeWave timeline engine server.
//   --port N     TCP port (default 7615)
//   --public     listen on all interfaces instead of localhost only
//   --start      start the clock immediately instead of waiting for the first player

int port = Protocol.DefaultPort;
bool listenPublic = false, autoStart = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--public": listenPublic = true; break;
        case "--start": autoStart = true; break;
        default: Console.WriteLine($"unknown argument {args[i]}"); return 2;
    }
}

var config = new ScenarioConfig();
var host = new EngineHost(config) { Running = autoStart };
var inbox = new ConcurrentQueue<(ClientConnection Client, Message? Message)>();
var clients = new List<ClientConnection>();

var listener = new TcpListener(listenPublic ? IPAddress.Any : IPAddress.Loopback, port);
listener.Start();
Console.WriteLine($"TimeWave engine listening on {(listenPublic ? "0.0.0.0" : "127.0.0.1")}:{port}");
Console.WriteLine($"window {config.WindowSeconds}s, present at {config.PresentOffsetSeconds}s, {config.BackgroundWaves} background waves at {config.BackgroundWaveRate}x, {config.TicksPerSecond} ticks/s");
Console.WriteLine(autoStart ? "clock running" : "waiting for the first player to start the clock");

new Thread(() =>
{
    while (true)
    {
        TcpClient tcp;
        try { tcp = listener.AcceptTcpClient(); }
        catch (SocketException) { return; }
        new ClientConnection(tcp, inbox).Start();
    }
}) { IsBackground = true, Name = "accept" }.Start();

bool quit = false;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; };

const double frameSeconds = 1.0 / 30.0;
var clock = Stopwatch.StartNew();
double last = 0, statsTimer = 0, logTimer = 0;
long lastSteps = 0;

while (!quit)
{
    double now = clock.Elapsed.TotalSeconds;
    double dt = now - last;
    if (dt < frameSeconds) { Thread.Sleep(1); continue; }
    last = now;
    dt = Math.Min(dt, 0.25); // after a stall, slow down rather than jump

    while (inbox.TryDequeue(out var item))
    {
        var client = item.Client;
        switch (item.Message)
        {
            case null:
                if (clients.Remove(client))
                {
                    host.Leave(client.Player);
                    Console.WriteLine($"player {client.Player} left ({client.Name})");
                }
                break;
            case Hello hello when client.Player < 0:
                if (hello.Version != Protocol.Version)
                {
                    client.Send(new Notice { Text = $"Protocol version {hello.Version} not supported (engine speaks {Protocol.Version})" });
                    client.Close();
                    break;
                }
                int slot = host.Join(hello.Name, hello.RequestedPlayer);
                if (slot < 0)
                {
                    client.Send(new Notice { Text = "Game is full" });
                    client.Close();
                    break;
                }
                client.Player = slot;
                client.Name = hello.Name;
                clients.Add(client);
                client.Send(host.BuildWelcome(slot));
                client.Send(host.BuildStats());
                Console.WriteLine($"player {slot} joined: {hello.Name}");
                break;
            case Message message when client.Player >= 0:
                host.Handle(client.Player, message);
                break;
        }
    }

    host.Update(dt);

    while (host.TryDequeueNotice(out int player, out string text))
        foreach (var c in clients)
            if (c.Player == player) c.Send(new Notice { Text = text });

    foreach (var c in clients) c.Send(host.BuildFrame(c.Player));

    statsTimer += dt;
    if (statsTimer >= 0.5)
    {
        statsTimer = 0;
        var stats = host.BuildStats();
        foreach (var c in clients) c.Send(stats);
    }

    logTimer += dt;
    if (logTimer >= 10 && host.Running)
    {
        var e = host.Engine;
        long steps = e.StepsTotal - lastSteps;
        lastSteps = e.StepsTotal;
        string waves = string.Join(" ", e.Waves.Select(w => $"{w.Kind.ToString()[0]}{(w.Owner >= 0 ? w.Owner.ToString() : "")}@{w.Tick}"));
        Console.WriteLine($"present {e.PresentTick / config.TicksPerSecond}s window [{e.WindowStart},{e.WindowEnd}) steps/s {steps / logTimer:F0} waves {waves} clients {clients.Count}");
        logTimer = 0;
    }
}

listener.Stop();
foreach (var c in clients) c.Close();
Console.WriteLine("stopped");
return 0;
