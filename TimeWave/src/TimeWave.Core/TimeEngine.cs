using System;
using System.Collections.Generic;
using TimeWave.Shared;

namespace TimeWave.Core
{
    /// <summary>A simulation cursor moving through the timeline at its own rate (Achron's "updater").</summary>
    public sealed class TimeWave
    {
        public int Id;
        public WaveKind Kind;
        public int Owner = -1;
        /// <summary>Ticks of wave time per tick of real time.</summary>
        public double Rate;
        /// <summary>Tick the wave should have reached; the scheduler steps it until <see cref="Tick"/> catches up.</summary>
        public double Target;
        public WorldState State;

        public int Tick => State.Tick;
    }

    /// <summary>Where one player is on the timeline.</summary>
    public sealed class PlayerTime
    {
        public int Player;
        /// <summary>The wave this player watches; the shared present wave unless they travelled.</summary>
        public TimeWave Wave;
        public TimeRate Rate = TimeRate.Normal;
        public float Chrono;
        internal bool WasBehindPresent;
    }

    /// <summary>
    /// Runs every timewave against the shared <see cref="Timeline"/>.
    ///
    /// Each update: waves get a new target time (rate x elapsed time); then the scheduler repeatedly
    /// steps the wave that is earliest in time, so history is always rewritten before a later wave reads it.
    /// A wave that reaches another wave's tick from behind carries newer history, so the other wave adopts
    /// its state ("overtake"). Background waves move faster than time and loop from the start of the window
    /// to its end, which is how a change made in the past spreads, wave by wave, up to the present and future.
    /// </summary>
    public sealed class TimeEngine
    {
        public readonly ScenarioConfig Config;
        public readonly Timeline Timeline;
        public readonly TimeWave Present;
        public readonly PlayerTime[] Players;

        readonly List<TimeWave> _waves = new List<TimeWave>();
        int _nextWaveId;

        public IReadOnlyList<TimeWave> Waves => _waves;
        /// <summary>Real time elapsed, in ticks; the present wave aims for it.</summary>
        public double PresentTime { get; private set; }
        public int PresentTick => Present.Tick;
        public int WindowStart { get; private set; }
        public int WindowEnd { get; private set; }
        public long StepsTotal { get; private set; }

        /// <summary>Raised with (player, text) for events a player should be told about.</summary>
        public event Action<int, string> Notice;

        public TimeEngine(ScenarioConfig config, WorldState initial)
        {
            Config = config;
            Timeline = new Timeline(config, initial);
            Present = AddWave(WaveKind.Present, -1, initial.Clone(), 1.0);
            for (int i = 0; i < config.BackgroundWaves; i++)
            {
                var w = AddWave(WaveKind.Background, -1, initial.Clone(), config.BackgroundWaveRate);
                // Stagger the waves evenly across the window; a negative target means "not started yet".
                w.Target = -(double)i * config.WindowTicks / config.BackgroundWaves;
            }
            Players = new PlayerTime[config.PlayerCount];
            for (int i = 0; i < Players.Length; i++)
                Players[i] = new PlayerTime { Player = i, Wave = Present, Chrono = config.ChronoInitial };
            UpdateWindow();
        }

        TimeWave AddWave(WaveKind kind, int owner, WorldState state, double rate)
        {
            var w = new TimeWave { Id = _nextWaveId++, Kind = kind, Owner = owner, State = state, Rate = rate, Target = state.Tick };
            _waves.Add(w);
            return w;
        }

        /// <summary>Advances real time by <paramref name="ticks"/> (fractional) and runs the waves.</summary>
        public void Advance(double ticks)
        {
            PresentTime += ticks;
            Present.Target = PresentTime;
            UpdateWindow();

            foreach (var w in _waves)
            {
                if (w == Present) continue;
                w.Target = Math.Min(w.Target + ticks * w.Rate, WindowEnd);
            }
            foreach (var w in _waves)
                if (w.Kind == WaveKind.Background && w.Tick >= WindowEnd)
                    Restart(w);

            UpdatePlayers(ticks);
            RunScheduler();
        }

        void UpdateWindow()
        {
            WindowStart = Math.Max(0, (int)PresentTime - Config.PresentOffsetTicks);
            WindowEnd = WindowStart + Config.WindowTicks;
            Timeline.Commit(WindowStart);
        }

        /// <summary>A background wave that reached the front of the window starts a new sweep from its beginning.</summary>
        void Restart(TimeWave w)
        {
            var s = Timeline.LoadAtOrBefore(WindowStart);
            if (s == null) return;
            w.State = s;
            w.Target = s.Tick;
        }

        void RunScheduler()
        {
            for (int budget = Config.MaxStepsPerUpdate; budget > 0; budget--)
            {
                TimeWave next = null;
                foreach (var w in _waves)
                {
                    if (w.Tick >= (int)Math.Floor(w.Target)) continue;
                    if (next == null || w.Tick < next.Tick || (w.Tick == next.Tick && w.Id < next.Id)) next = w;
                }
                if (next == null) return;

                Step(next);

                // `next` arrived at this tick from behind, so it has seen every command up to here:
                // any wave already sitting at this tick takes over its state.
                foreach (var w in _waves)
                    if (w != next && w.Tick == next.Tick)
                        w.State = next.State.Clone();
            }
        }

        void Step(TimeWave w)
        {
            int tick = w.Tick;
            var stats = new TickStats(Config.PlayerCount);
            Simulation.Step(w.State, Timeline.CommandsAt(tick), Config, stats);
            Timeline.WriteStats(tick, stats);
            if (w.Tick % Config.KeyframeInterval == 0) Timeline.WriteKeyframe(w.State);
            StepsTotal++;
        }

        // ---- players -----------------------------------------------------------------------

        public bool IsAtPresent(PlayerTime p) => p.Wave == Present;

        public void ReturnToPresent(PlayerTime p)
        {
            if (p.Wave != Present)
            {
                _waves.Remove(p.Wave);
                p.Wave = Present;
            }
            p.Rate = TimeRate.Normal;
        }

        public void SetRate(PlayerTime p, TimeRate rate)
        {
            if (rate == TimeRate.Normal && Math.Abs(p.Wave.Tick - PresentTick) <= Config.PresentSnapTicks)
            {
                ReturnToPresent(p);
                return;
            }
            double mult = TimeRates.Multiplier(rate);
            if (p.Wave == Present)
            {
                p.Wave = AddWave(WaveKind.Player, p.Player, Present.State.Clone(), mult);
                // Start from the present's exact (fractional) time so the new wave never lags it by a tick.
                p.Wave.Target = PresentTime;
                p.WasBehindPresent = false;
            }
            else
            {
                p.Wave.Rate = mult;
                p.Wave.Target = p.Wave.Tick;
            }
            p.Rate = rate;
        }

        /// <summary>Moves a player to <paramref name="tick"/>, keeping their time rate. Returns false if that time is no longer stored.</summary>
        public bool JumpTo(PlayerTime p, int tick)
        {
            tick = IntMath.Clamp(tick, Math.Max(WindowStart, Timeline.EarliestKeyframe), WindowEnd - 1);
            if (Math.Abs(tick - PresentTick) <= Config.PresentSnapTicks)
            {
                ReturnToPresent(p);
                return true;
            }

            // Start from a wave just behind the target if there is one, otherwise from the nearest keyframe.
            WorldState state = null;
            foreach (var w in _waves)
                if (w.Tick <= tick && tick - w.Tick < Config.KeyframeInterval && (state == null || w.Tick > state.Tick))
                    state = w.State;
            state = state != null ? state.Clone() : Timeline.LoadAtOrBefore(tick);
            if (state == null) return false;

            double rate = TimeRates.Multiplier(p.Rate);
            TimeWave wave;
            if (p.Wave.Kind == WaveKind.Player)
            {
                wave = p.Wave;
                wave.State = state;
                wave.Rate = rate;
            }
            else
            {
                wave = AddWave(WaveKind.Player, p.Player, state, rate);
                p.Wave = wave;
            }
            while (wave.Tick < tick) Step(wave);
            wave.Target = wave.Tick;
            p.WasBehindPresent = IsBehindPresent(wave);
            return true;
        }

        /// <summary>
        /// Inserts an order at the tick the player is looking at. Orders in the present are free;
        /// orders in the past or future cost chronoenergy.
        /// </summary>
        public bool IssueCommand(PlayerTime p, OrderType order, int[] unitIds, int targetX, int targetY, int targetId, out string error)
        {
            error = null;
            float cost = p.Wave == Present ? 0f : Config.PastCommandCost;
            if (p.Chrono < cost)
            {
                error = "Not enough chronoenergy for an order outside the present";
                return false;
            }
            p.Chrono -= cost;
            Timeline.AddCommand(new Command
            {
                Tick = p.Wave.Tick,
                Player = p.Player,
                Order = order,
                UnitIds = (int[])(unitIds ?? Array.Empty<int>()).Clone(),
                TargetX = targetX,
                TargetY = targetY,
                TargetId = targetId,
            });
            return true;
        }

        /// <summary>Clearly in the past: a lag of a tick or two from scheduling order does not count.</summary>
        bool IsBehindPresent(TimeWave w) => w.Tick < PresentTick - Config.PresentSnapTicks;

        void UpdatePlayers(double ticks)
        {
            float seconds = (float)(ticks / Config.TicksPerSecond);
            foreach (var p in Players)
            {
                if (p.Wave == Present)
                {
                    p.Chrono = Math.Min(Config.ChronoMax, p.Chrono + Config.PresentGainPerSecond * seconds);
                    continue;
                }

                float drain = p.Wave.Rate == 0 ? Config.StoppedDrainPerSecond
                    : p.Wave.Tick < PresentTick ? Config.PastDrainPerSecond : Config.FutureDrainPerSecond;
                p.Chrono = Math.Max(0f, p.Chrono - drain * seconds);

                if (p.Chrono <= 0f)
                {
                    ReturnToPresent(p);
                    Notice?.Invoke(p.Player, "Out of chronoenergy: pulled back to the present");
                    continue;
                }

                bool behind = IsBehindPresent(p.Wave);
                if (p.WasBehindPresent && !behind && p.Wave.Rate >= 1.0)
                {
                    ReturnToPresent(p);
                    Notice?.Invoke(p.Player, "Caught up with the present");
                    continue;
                }
                p.WasBehindPresent = behind;

                if (p.Wave.Tick < WindowStart)
                {
                    var s = Timeline.LoadAtOrBefore(WindowStart);
                    if (s != null) { p.Wave.State = s; p.Wave.Target = s.Tick; }
                    p.Wave.Rate = 1.0;
                    p.Rate = TimeRate.Normal;
                    Notice?.Invoke(p.Player, "Reached the start of the time window");
                }
                else if (p.Wave.Tick >= WindowEnd - 1 && p.Wave.Rate > 1.0)
                {
                    p.Wave.Rate = 1.0;
                    p.Rate = TimeRate.Normal;
                    Notice?.Invoke(p.Player, "Reached the end of the time window");
                }
            }
        }
    }
}
