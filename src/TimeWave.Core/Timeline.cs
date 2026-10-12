using System;
using System.Collections.Generic;
using TimeWave.Shared;

namespace TimeWave.Core
{
    /// <summary>
    /// The shared history every timewave reads and writes.
    /// Commands are permanent inputs; keyframes and stats are derived and simply overwritten
    /// by whichever wave passes that tick last (the most up-to-date one).
    /// </summary>
    public sealed class Timeline
    {
        readonly ScenarioConfig _config;
        readonly Dictionary<int, WorldState> _keyframes = new Dictionary<int, WorldState>();
        readonly SortedDictionary<int, List<Command>> _commands = new SortedDictionary<int, List<Command>>();
        readonly Dictionary<int, TickStats> _stats = new Dictionary<int, TickStats>();
        long _seq;

        /// <summary>Oldest tick that can still be loaded; everything before it is committed for good.</summary>
        public int EarliestKeyframe { get; private set; }

        public Timeline(ScenarioConfig config, WorldState initial)
        {
            _config = config;
            EarliestKeyframe = initial.Tick;
            WriteKeyframe(initial);
        }

        public int KeyframeFloor(int tick) => tick - ((tick % _config.KeyframeInterval) + _config.KeyframeInterval) % _config.KeyframeInterval;

        public void WriteKeyframe(WorldState state)
        {
            if (state.Tick < EarliestKeyframe) return;
            _keyframes[state.Tick] = state.Clone();
        }

        /// <summary>A copy of the newest keyframe at or before <paramref name="tick"/>, or null if that part of history is gone.</summary>
        public WorldState LoadAtOrBefore(int tick)
        {
            for (int k = KeyframeFloor(tick); k >= EarliestKeyframe; k -= _config.KeyframeInterval)
                if (_keyframes.TryGetValue(k, out var s)) return s.Clone();
            return null;
        }

        public void AddCommand(Command command)
        {
            command.Seq = ++_seq;
            if (!_commands.TryGetValue(command.Tick, out var list))
                _commands[command.Tick] = list = new List<Command>();
            int at = list.Count;
            while (at > 0 && list[at - 1].Player > command.Player) at--;
            list.Insert(at, command);
        }

        public IReadOnlyList<Command> CommandsAt(int tick) => _commands.TryGetValue(tick, out var list) ? list : null;

        public IEnumerable<KeyValuePair<int, List<Command>>> AllCommands => _commands;

        public void WriteStats(int tick, TickStats stats) => _stats[tick] = stats;

        /// <summary>Permanently drops history older than the keyframe at or before <paramref name="windowStart"/>.</summary>
        public void Commit(int windowStart)
        {
            int keep = KeyframeFloor(windowStart);
            if (keep <= EarliestKeyframe) return;
            if (!_keyframes.ContainsKey(keep)) return; // never commit past the last loadable snapshot
            EarliestKeyframe = keep;

            var old = new List<int>();
            foreach (var k in _keyframes.Keys) if (k < keep) old.Add(k);
            foreach (var k in old) _keyframes.Remove(k);

            old.Clear();
            foreach (var k in _commands.Keys) { if (k < keep) old.Add(k); else break; }
            foreach (var k in old) _commands.Remove(k);

            old.Clear();
            foreach (var k in _stats.Keys) if (k < keep) old.Add(k);
            foreach (var k in old) _stats.Remove(k);
        }

        public TimelineStats BuildStats(int windowStart, int windowEnd)
        {
            int bucket = _config.StatsBucketTicks;
            int players = _config.PlayerCount;
            int first = windowStart - windowStart % bucket;
            int count = Math.Max(1, (windowEnd - first + bucket - 1) / bucket);
            var m = new TimelineStats
            {
                FirstTick = first, BucketTicks = bucket, BucketCount = count, PlayerCount = players,
                UnitCount = new ushort[count * players], DamageDealt = new ushort[count * players],
                DamageTaken = new ushort[count * players], Created = new byte[count * players], Commands = new byte[count * players],
            };
            for (int b = 0; b < count; b++)
            {
                for (int t = first + b * bucket, end = t + bucket; t < end; t++)
                {
                    if (_stats.TryGetValue(t, out var s))
                    {
                        for (int p = 0; p < players; p++)
                        {
                            int i = b * players + p;
                            m.UnitCount[i] = (ushort)Math.Max(m.UnitCount[i], Math.Min(s.UnitCount[p], ushort.MaxValue));
                            m.DamageDealt[i] = Sat16(m.DamageDealt[i] + s.DamageDealt[p]);
                            m.DamageTaken[i] = Sat16(m.DamageTaken[i] + s.DamageTaken[p]);
                            m.Created[i] = Sat8(m.Created[i] + s.Created[p]);
                        }
                    }
                    if (_commands.TryGetValue(t, out var cmds))
                        foreach (var c in cmds)
                            if (c.Player >= 0 && c.Player < players)
                                m.Commands[b * players + c.Player] = Sat8(m.Commands[b * players + c.Player] + 1);
                }
            }
            return m;
        }

        static ushort Sat16(int v) => (ushort)Math.Min(v, ushort.MaxValue);
        static byte Sat8(int v) => (byte)Math.Min(v, byte.MaxValue);
    }
}
