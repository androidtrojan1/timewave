using System;

namespace TimeWave.Shared
{
    public enum UnitType : byte { Soldier = 0, Tank = 1, Factory = 2 }

    public enum OrderType : byte { None = 0, Move = 1, Attack = 2, AttackMove = 3, Stop = 4 }

    /// <summary>How fast a player moves through time (same four speeds as Achron).</summary>
    public enum TimeRate : byte { Paused = 0, Half = 1, Normal = 2, Fast = 3 }

    public enum WaveKind : byte { Background = 0, Present = 1, Player = 2 }

    public static class World
    {
        /// <summary>Positions are integers in milli-tiles: 1 tile = 1000 units. Keeps the simulation deterministic.</summary>
        public const int TileSize = 1000;
    }

    public static class TimeRates
    {
        public static double Multiplier(TimeRate rate)
        {
            switch (rate)
            {
                case TimeRate.Paused: return 0.0;
                case TimeRate.Half: return 0.5;
                case TimeRate.Fast: return 2.0;
                default: return 1.0;
            }
        }
    }

    [Flags]
    public enum UnitFlags : byte { None = 0, Firing = 1, Moving = 2 }

    /// <summary>What a client sees of one unit at the tick its player is looking at.</summary>
    public struct UnitSnapshot
    {
        public int Id;
        /// <summary>Identity that survives time travel; equals Id for units that never chronoported.</summary>
        public int Name;
        public byte Owner;
        public UnitType Type;
        public int X, Y;
        public short Hp, MaxHp;
        public OrderType Order;
        public int TargetX, TargetY, TargetId;
        public UnitFlags Flags;
    }

    public struct WaveInfo
    {
        public int Tick;
        public float Rate;
        public WaveKind Kind;
        /// <summary>Owning player for player waves, -1 otherwise.</summary>
        public sbyte Owner;
    }

    public struct PlayerTimeInfo
    {
        public byte Player;
        public int Tick;
        public TimeRate Rate;
        public bool AtPresent;
        public bool Human;
    }
}
