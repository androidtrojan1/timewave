using System;
using System.Collections.Generic;
using TimeWave.Shared;

namespace TimeWave.Core
{
    /// <summary>
    /// Transport-independent game session: seats players, translates protocol messages into engine calls,
    /// runs a simple opponent for empty seats and builds the frames clients receive.
    /// The TCP server and the in-process <see cref="EmbeddedLink"/> both sit on top of this.
    /// </summary>
    public sealed class EngineHost
    {
        public readonly ScenarioConfig Config;
        public readonly TimeEngine Engine;
        readonly string[] _names;
        readonly bool[] _human;
        readonly Queue<KeyValuePair<int, string>> _notices = new Queue<KeyValuePair<int, string>>();
        int _nextAiDecision;

        /// <summary>The clock starts when the first human joins.</summary>
        public bool Running { get; set; }

        public EngineHost(ScenarioConfig config = null)
        {
            Config = config ?? new ScenarioConfig();
            Engine = new TimeEngine(Config, Scenarios.Skirmish(Config));
            _names = new string[Config.PlayerCount];
            _human = new bool[Config.PlayerCount];
            for (int i = 0; i < _names.Length; i++) _names[i] = "AI " + (i + 1);
            Engine.Notice += (player, text) => _notices.Enqueue(new KeyValuePair<int, string>(player, text));
            _nextAiDecision = Config.AiDecisionTicks;
        }

        public bool IsHuman(int player) => _human[player];

        /// <summary>Seats a human; returns the player slot or -1 if the game is full.</summary>
        public int Join(string name, int requested)
        {
            int slot = -1;
            if (requested >= 0 && requested < _human.Length && !_human[requested]) slot = requested;
            for (int i = 0; slot < 0 && i < _human.Length; i++) if (!_human[i]) slot = i;
            if (slot < 0) return -1;
            _human[slot] = true;
            _names[slot] = string.IsNullOrEmpty(name) ? "Player " + (slot + 1) : name;
            Running = true;
            return slot;
        }

        public void Leave(int player)
        {
            if (player < 0 || player >= _human.Length) return;
            _human[player] = false;
            _names[player] = "AI " + (player + 1);
            Engine.ReturnToPresent(Engine.Players[player]);
        }

        public void Handle(int player, Message message)
        {
            if (player < 0 || player >= _human.Length || !_human[player]) return;
            var p = Engine.Players[player];
            switch (message)
            {
                case IssueCommand c:
                    if (!Engine.IssueCommand(p, c.Order, c.UnitIds, c.TargetX, c.TargetY, c.TargetId, out var error))
                        _notices.Enqueue(new KeyValuePair<int, string>(player, error));
                    break;
                case JumpTo j:
                    if (!Engine.JumpTo(p, j.Tick))
                        _notices.Enqueue(new KeyValuePair<int, string>(player, "That time is no longer in the timeline"));
                    break;
                case SetRate s:
                    Engine.SetRate(p, s.Rate);
                    break;
                case JumpToPresent _:
                    Engine.ReturnToPresent(p);
                    break;
            }
        }

        public void Update(double seconds)
        {
            if (!Running) return;
            Engine.Advance(seconds * Config.TicksPerSecond);
            RunAi();
        }

        public bool TryDequeueNotice(out int player, out string text)
        {
            if (_notices.Count == 0) { player = -1; text = null; return false; }
            var n = _notices.Dequeue();
            player = n.Key;
            text = n.Value;
            return true;
        }

        /// <summary>Opponent for empty seats: every few seconds, sends idle units at the enemy factory. It plays only in the present.</summary>
        void RunAi()
        {
            if (Engine.PresentTick < _nextAiDecision) return;
            _nextAiDecision = Engine.PresentTick + Config.AiDecisionTicks;
            var world = Engine.Present.State;
            for (int player = 0; player < _human.Length; player++)
            {
                if (_human[player]) continue;
                var idle = new List<int>();
                int targetX = -1, targetY = -1;
                foreach (var u in world.Units)
                {
                    if (u.Owner == player && u.Type != UnitType.Factory && u.Order == OrderType.None) idle.Add(u.Id);
                    if (u.Owner != player && u.Type == UnitType.Factory) { targetX = u.X; targetY = u.Y; }
                }
                if (idle.Count < 3 || targetX < 0) continue;
                var p = Engine.Players[player];
                Engine.ReturnToPresent(p);
                Engine.IssueCommand(p, OrderType.AttackMove, idle.ToArray(), targetX, targetY, 0, out _);
            }
        }

        public Welcome BuildWelcome(int player) => new Welcome
        {
            PlayerId = player,
            PlayerCount = Config.PlayerCount,
            MapWidth = Config.MapWidth,
            MapHeight = Config.MapHeight,
            TicksPerSecond = Config.TicksPerSecond,
            WindowTicks = Config.WindowTicks,
            PresentOffsetTicks = Config.PresentOffsetTicks,
            ChronoMax = Config.ChronoMax,
            PlayerNames = (string[])_names.Clone(),
        };

        public WorldFrame BuildFrame(int player)
        {
            var p = Engine.Players[player];
            var waves = new WaveInfo[Engine.Waves.Count];
            for (int i = 0; i < waves.Length; i++)
            {
                var w = Engine.Waves[i];
                waves[i] = new WaveInfo { Tick = w.Tick, Rate = (float)w.Rate, Kind = w.Kind, Owner = (sbyte)w.Owner };
            }
            var players = new PlayerTimeInfo[Engine.Players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                var o = Engine.Players[i];
                players[i] = new PlayerTimeInfo { Player = (byte)i, Tick = o.Wave.Tick, Rate = o.Rate, AtPresent = Engine.IsAtPresent(o), Human = _human[i] };
            }
            return new WorldFrame
            {
                PlayerTick = p.Wave.Tick,
                Rate = p.Rate,
                PresentTick = Engine.PresentTick,
                WindowStart = Engine.WindowStart,
                WindowEnd = Engine.WindowEnd,
                Chrono = p.Chrono,
                ChronoMax = Config.ChronoMax,
                Waves = waves,
                Players = players,
                Units = p.Wave.State.ToSnapshots(),
            };
        }

        public TimelineStats BuildStats() => Engine.Timeline.BuildStats(Engine.WindowStart, Engine.WindowEnd);
    }
}
