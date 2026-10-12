using System.Collections.Concurrent;
using System.Diagnostics;
using TimeWave.Shared;

// Text client for the TimeWave engine, for trying the protocol without Unity.
//   TimeWave.ConsoleClient [--host H] [--port N] [--name NAME] [--smoke]
// --smoke runs a scripted check against a running server and exits with 0 on success.

string hostName = "127.0.0.1";
int port = Protocol.DefaultPort;
string name = "console";
bool smoke = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host": hostName = args[++i]; break;
        case "--port": port = int.Parse(args[++i]); break;
        case "--name": name = args[++i]; break;
        case "--smoke": smoke = true; break;
    }
}

using var link = new TimeWaveClient();
try { link.Connect(hostName, port, name); }
catch (Exception e)
{
    Console.WriteLine($"cannot connect to {hostName}:{port}: {e.Message}");
    return 1;
}

var state = new ClientState();
return smoke ? Smoke(link, state) : Interactive(link, state);

static int Interactive(TimeWaveClient link, ClientState s)
{
    var lines = new ConcurrentQueue<string>();
    new Thread(() => { string? l; while ((l = Console.ReadLine()) != null) lines.Enqueue(l); }) { IsBackground = true }.Start();
    Console.WriteLine("commands: status | rate 0..3 | jump <seconds from present, e.g. -60> | present | attack | move <x> <y> | stop | quit");
    var statusTimer = Stopwatch.StartNew();
    while (link.Connected)
    {
        s.Drain(link, print: true);
        while (lines.TryDequeue(out var line))
        {
            if (!Execute(link, s, line.Trim())) return 0;
        }
        if (statusTimer.Elapsed.TotalSeconds >= 3 && s.Frame != null) { Console.WriteLine(s.Status()); statusTimer.Restart(); }
        Thread.Sleep(15);
    }
    Console.WriteLine($"disconnected: {link.Error}");
    return 1;
}

static bool Execute(TimeWaveClient link, ClientState s, string line)
{
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0 || s.Frame == null || s.Welcome == null) return true;
    int tps = s.Welcome.TicksPerSecond;
    switch (parts[0])
    {
        case "quit": return false;
        case "status": Console.WriteLine(s.Status()); break;
        case "rate": link.Send(new SetRate { Rate = (TimeRate)int.Parse(parts[1]) }); break;
        case "jump": link.Send(new JumpTo { Tick = s.Frame.PresentTick + (int)(double.Parse(parts[1]) * tps) }); break;
        case "present": link.Send(new JumpToPresent()); break;
        case "stop": link.Send(new IssueCommand { Order = OrderType.Stop, UnitIds = s.MyMobileUnits() }); break;
        case "move":
            link.Send(new IssueCommand { Order = OrderType.Move, UnitIds = s.MyMobileUnits(), TargetX = (int)(double.Parse(parts[1]) * World.TileSize), TargetY = (int)(double.Parse(parts[2]) * World.TileSize) });
            break;
        case "attack":
            var f = s.EnemyFactory();
            if (f.HasValue) link.Send(new IssueCommand { Order = OrderType.AttackMove, UnitIds = s.MyMobileUnits(), TargetX = f.Value.X, TargetY = f.Value.Y });
            break;
        default: Console.WriteLine("unknown command"); break;
    }
    return true;
}

static int Smoke(TimeWaveClient link, ClientState s)
{
    bool Wait(Func<bool> condition, double seconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            s.Drain(link, print: true);
            if (condition()) return true;
            if (!link.Connected) return false;
            Thread.Sleep(10);
        }
        return false;
    }
    int failures = 0;
    void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

    Check(Wait(() => s.Welcome != null && s.Frame != null, 5), "welcome and first frame");
    if (failures > 0) return 1;
    Console.WriteLine($"seated as player {s.Welcome!.PlayerId}");

    int start = s.Frame!.PresentTick;
    Check(Wait(() => s.Frame!.PresentTick >= start + 20, 5), "present advances");
    Check(Wait(() => s.Stats != null, 2), "timeline stats received");

    link.Send(new SetRate { Rate = TimeRate.Fast });
    Check(Wait(() => s.Frame!.PlayerTick > s.Frame.PresentTick + 15, 5), "fast-forward moves the player into the future");

    link.Send(new JumpTo { Tick = Math.Max(1, s.Frame!.PresentTick - 30) });
    Check(Wait(() => s.Frame!.PlayerTick < s.Frame.PresentTick - 10, 3), "jump into the past");

    float chrono = s.Frame!.Chrono;
    link.Send(new IssueCommand { Order = OrderType.Move, UnitIds = s.MyMobileUnits(), TargetX = 3000, TargetY = 3000 });
    Check(Wait(() => s.Frame!.Chrono < chrono - 3, 3), "order in the past costs chronoenergy");

    link.Send(new JumpToPresent());
    Check(Wait(() => s.Frame!.Players[s.Welcome.PlayerId].AtPresent, 3), "back to the present");
    Check(s.Frame!.Waves.Count(w => w.Kind == WaveKind.Background) >= 1, "background waves reported");

    Console.WriteLine(s.Status());
    Console.WriteLine(failures == 0 ? "SMOKE OK" : $"SMOKE FAILED ({failures})");
    return failures == 0 ? 0 : 1;
}

sealed class ClientState
{
    public Welcome? Welcome;
    public WorldFrame? Frame;
    public TimelineStats? Stats;

    public void Drain(TimeWaveClient link, bool print)
    {
        while (link.TryReceive(out var m))
        {
            switch (m)
            {
                case Welcome w: Welcome = w; break;
                case WorldFrame f: Frame = f; break;
                case TimelineStats st: Stats = st; break;
                case Notice n: if (print) Console.WriteLine($"[engine] {n.Text}"); break;
            }
        }
    }

    public int[] MyMobileUnits() =>
        Frame!.Units.Where(u => u.Owner == Welcome!.PlayerId && u.Type != UnitType.Factory).Select(u => u.Id).ToArray();

    public UnitSnapshot? EnemyFactory()
    {
        foreach (var u in Frame!.Units)
            if (u.Owner != Welcome!.PlayerId && u.Type == UnitType.Factory) return u;
        return null;
    }

    string Clock(int ticks)
    {
        int tps = Welcome!.TicksPerSecond;
        int sec = Math.Abs(ticks) / tps;
        return $"{(ticks < 0 ? "-" : "+")}{sec / 60:00}:{sec % 60:00}";
    }

    public string Status()
    {
        var f = Frame!;
        int me = Welcome!.PlayerId;
        int mine = f.Units.Count(u => u.Owner == me), enemy = f.Units.Length - mine;
        string waves = string.Join(" ", f.Waves.Select(w => $"{w.Kind.ToString()[0]}{Clock(w.Tick - f.PresentTick)}"));
        return $"you {Clock(f.PlayerTick - f.PresentTick)} vs present (present {f.PresentTick / Welcome.TicksPerSecond}s) rate {f.Rate} | CE {f.Chrono:F0}/{f.ChronoMax:F0} | units {mine} vs {enemy} | waves {waves}";
    }
}
