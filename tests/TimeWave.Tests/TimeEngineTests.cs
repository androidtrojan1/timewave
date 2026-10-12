using System.Linq;
using TimeWave.Core;
using TimeWave.Shared;

namespace TimeWave.Tests;

public class TimeEngineTests
{
    const double Frame = 1.0 / 30.0;

    static EngineHost StartedHost(ScenarioConfig? config = null)
    {
        var host = new EngineHost(config ?? new ScenarioConfig()) { Running = true };
        return host;
    }

    static void Run(EngineHost host, double seconds, double frame = Frame)
    {
        for (double t = 0; t < seconds; t += frame) host.Update(frame);
    }

    /// <summary>The oracle: replay every logged command from tick 0 in a single straight simulation.</summary>
    static WorldState Resimulate(ScenarioConfig config, Timeline timeline, int toTick)
    {
        var world = Scenarios.Skirmish(config);
        while (world.Tick < toTick)
            Simulation.Step(world, timeline.CommandsAt(world.Tick), config, new TickStats(config.PlayerCount));
        return world;
    }

    [Fact]
    public void Simulation_is_deterministic()
    {
        var config = new ScenarioConfig();
        var a = Scenarios.Skirmish(config);
        var b = Scenarios.Skirmish(config);
        var cmd = new Command { Player = 0, Order = OrderType.AttackMove, UnitIds = Enumerable.Range(10, 10).ToArray(), TargetX = 43000, TargetY = 16000 };
        var cmds = new[] { cmd };
        for (int i = 0; i < 1200; i++)
        {
            Simulation.Step(a, i == 0 ? cmds : null, config, new TickStats(2));
            Simulation.Step(b, i == 0 ? cmds : null, config, new TickStats(2));
        }
        Assert.Equal(a.Hash(), b.Hash());
        Assert.NotEqual(Scenarios.Skirmish(config).Units.Count, a.Units.Count); // the battle actually happened
    }

    [Fact]
    public void Present_equals_straight_resimulation_when_nobody_travels()
    {
        var host = StartedHost();
        Run(host, 60);
        var engine = host.Engine;
        Assert.InRange(engine.PresentTick, 595, 601);
        Assert.Equal(Resimulate(host.Config, engine.Timeline, engine.PresentTick).Hash(), engine.Present.State.Hash());
    }

    [Fact]
    public void Order_in_the_past_reaches_the_present_only_through_timewaves()
    {
        var host = StartedHost();
        Run(host, 50);
        var engine = host.Engine;
        var p = engine.Players[0];

        Assert.True(engine.JumpTo(p, 200));
        Assert.Equal(200, p.Wave.Tick);
        var ids = p.Wave.State.Units.Where(u => u.Owner == 0 && u.Type != UnitType.Factory).Select(u => u.Id).ToArray();
        Assert.True(engine.IssueCommand(p, OrderType.Move, ids, 2000, 2000, 0, out _));
        Assert.Equal(host.Config.ChronoMax - host.Config.PastCommandCost, p.Chrono, 0.5f); // 50 s at present filled the bar
        engine.ReturnToPresent(p);

        // Right after the order the present still shows the old timeline...
        Assert.NotEqual(Resimulate(host.Config, engine.Timeline, engine.PresentTick).Hash(), engine.Present.State.Hash());

        // ...and after background waves have swept from tick 200 to the present, it matches the new one exactly.
        Run(host, 150);
        Assert.Equal(Resimulate(host.Config, engine.Timeline, engine.PresentTick).Hash(), engine.Present.State.Hash());
    }

    [Fact]
    public void Order_in_the_past_applies_at_once_on_the_players_own_wave()
    {
        var host = new EngineHost();
        int me = host.Join("tester", -1);
        Run(host, 10);
        host.Handle(me, new JumpTo { Tick = host.Engine.PresentTick - 60 });
        var p = host.Engine.Players[me];
        var ids = p.Wave.State.Units.Where(u => u.Owner == me && u.Type == UnitType.Soldier).Select(u => u.Id).ToArray();
        double Avg() => p.Wave.State.Units.Where(u => ids.Contains(u.Id)).Average(u => Math.Sqrt(IntMath.DistSq(u.X, u.Y, 3000, 3000)));
        double before = Avg();
        host.Handle(me, new IssueCommand { Order = OrderType.Move, UnitIds = ids, TargetX = 3000, TargetY = 3000 });
        Run(host, 3);
        Assert.False(host.Engine.IsAtPresent(p));
        Assert.True(Avg() < before - 2000, $"soldiers should have walked ~3 tiles toward the target: {before:F0} -> {Avg():F0}");
    }

    [Fact]
    public void Running_out_of_chronoenergy_in_the_past_pulls_the_player_back()
    {
        var config = new ScenarioConfig { ChronoInitial = 3f, PresentGainPerSecond = 0f, PastDrainPerSecond = 1f };
        var host = StartedHost(config);
        Run(host, 30);
        var p = host.Engine.Players[0];
        Assert.True(host.Engine.JumpTo(p, 50));
        Assert.False(host.Engine.IsAtPresent(p));
        Run(host, 5);
        Assert.True(host.Engine.IsAtPresent(p));
    }

    [Fact]
    public void Time_rates_move_the_player_relative_to_the_present()
    {
        var host = StartedHost();
        Run(host, 10);
        var engine = host.Engine;
        var p = engine.Players[0];

        engine.SetRate(p, TimeRate.Fast);
        Run(host, 5);
        Assert.InRange(p.Wave.Tick - engine.PresentTick, 40, 60); // 2x for 5 s = 50 ticks ahead

        engine.SetRate(p, TimeRate.Paused);
        int frozen = p.Wave.Tick;
        Run(host, 2);
        Assert.Equal(frozen, p.Wave.Tick);

        engine.ReturnToPresent(p);
        Assert.True(engine.IsAtPresent(p));
    }

    [Theory]
    [InlineData(1.0 / 30)]
    [InlineData(1.0 / 500)] // an embedded engine pumped every frame of a fast client
    public void Fast_forward_from_the_present_is_not_mistaken_for_catching_up(double frame)
    {
        var host = StartedHost();
        Run(host, 3.37, frame);
        var p = host.Engine.Players[0];
        host.Engine.SetRate(p, TimeRate.Fast);
        Run(host, 2, frame);
        Assert.False(host.Engine.IsAtPresent(p));
        Assert.InRange(p.Wave.Tick - host.Engine.PresentTick, 15, 25);
    }

    [Fact]
    public void History_before_the_window_is_committed()
    {
        var config = new ScenarioConfig { WindowSeconds = 30, PresentOffsetSeconds = 20 };
        var host = StartedHost(config);
        Run(host, 60);
        var timeline = host.Engine.Timeline;
        Assert.True(timeline.EarliestKeyframe > 0);
        Assert.Null(timeline.LoadAtOrBefore(0));
        Assert.NotNull(timeline.LoadAtOrBefore(host.Engine.WindowStart));
        Assert.True(host.Engine.JumpTo(host.Engine.Players[0], 0)); // clamped to the window start
        Assert.True(host.Engine.Players[0].Wave.Tick >= timeline.EarliestKeyframe);
    }
}
