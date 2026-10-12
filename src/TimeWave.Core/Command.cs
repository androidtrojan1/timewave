using TimeWave.Shared;

namespace TimeWave.Core
{
    /// <summary>
    /// A player order pinned to a tick. Orders are the only immutable inputs of the timeline;
    /// every unit state is derived from them and recomputed by each passing timewave.
    /// </summary>
    public sealed class Command
    {
        public int Tick;
        public int Player;
        /// <summary>Insertion order; with Player it fixes the order of commands within one tick.</summary>
        public long Seq;
        public OrderType Order;
        public int[] UnitIds;
        public int TargetX, TargetY, TargetId;
    }

    /// <summary>Per-player activity during one tick, aggregated into the timeline bands.</summary>
    public sealed class TickStats
    {
        public readonly int[] UnitCount;
        public readonly int[] DamageDealt;
        public readonly int[] DamageTaken;
        public readonly int[] Created;

        public TickStats(int players)
        {
            UnitCount = new int[players];
            DamageDealt = new int[players];
            DamageTaken = new int[players];
            Created = new int[players];
        }
    }
}
