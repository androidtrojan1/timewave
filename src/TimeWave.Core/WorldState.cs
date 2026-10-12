using System.Collections.Generic;
using TimeWave.Shared;

namespace TimeWave.Core
{
    /// <summary>The whole world at one tick. Units are kept sorted by Id so iteration order (and therefore the simulation) is deterministic.</summary>
    public sealed class WorldState
    {
        public int Tick;
        public List<UnitState> Units = new List<UnitState>();

        public WorldState Clone() => new WorldState { Tick = Tick, Units = new List<UnitState>(Units) };

        public int IndexOf(int id)
        {
            int lo = 0, hi = Units.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int v = Units[mid].Id;
                if (v == id) return mid;
                if (v < id) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        public void Insert(UnitState unit)
        {
            int lo = 0, hi = Units.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Units[mid].Id < unit.Id) lo = mid + 1; else hi = mid;
            }
            Units.Insert(lo, unit);
        }

        /// <summary>FNV-1a over every chronal field; equal hashes mean equal worlds for determinism checks.</summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; }
            }
            Mix(Tick);
            foreach (var u in Units)
            {
                Mix(u.Id); Mix(u.Name); Mix(u.Owner); Mix((long)u.Type); Mix(u.X); Mix(u.Y); Mix(u.Hp);
                Mix((long)u.Order); Mix(u.TargetX); Mix(u.TargetY); Mix(u.TargetId); Mix(u.Cooldown); Mix(u.Timer); Mix(u.Spawned);
            }
            return h;
        }

        public UnitSnapshot[] ToSnapshots()
        {
            var a = new UnitSnapshot[Units.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Units[i].ToSnapshot();
            return a;
        }
    }
}
