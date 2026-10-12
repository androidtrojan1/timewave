using TimeWave.Shared;

namespace TimeWave.Core
{
    public sealed class UnitDef
    {
        public short MaxHp;
        /// <summary>Milli-tiles per tick.</summary>
        public int Speed;
        public int Range;
        public int AcquireRange;
        public short Damage;
        public short Cooldown;
        /// <summary>Collision radius, also added to attackers' range.</summary>
        public int Radius;
        /// <summary>Factories only: ticks between spawned soldiers.</summary>
        public int SpawnTicks;

        static readonly UnitDef[] Defs =
        {
            new UnitDef { MaxHp = 80,  Speed = 100, Range = 3000, AcquireRange = 6000, Damage = 8,  Cooldown = 8,  Radius = 350 },  // Soldier
            new UnitDef { MaxHp = 220, Speed = 70,  Range = 4500, AcquireRange = 7000, Damage = 22, Cooldown = 15, Radius = 550 },  // Tank
            new UnitDef { MaxHp = 900, Speed = 0,   Range = 0,    AcquireRange = 0,    Damage = 0,  Cooldown = 0,  Radius = 1200, SpawnTicks = 200 }, // Factory
        };

        public static UnitDef Of(UnitType type) => Defs[(int)type];
    }

    /// <summary>Complete chronal state of one unit. A value type so world snapshots are cheap array copies.</summary>
    public struct UnitState
    {
        public int Id;
        public int Name;
        public byte Owner;
        public UnitType Type;
        public int X, Y;
        public short Hp;
        public OrderType Order;
        public int TargetX, TargetY, TargetId;
        public short Cooldown;
        /// <summary>Factories: ticks since the last spawn.</summary>
        public int Timer;
        /// <summary>Factories: soldiers spawned so far, which makes spawned ids identical on every timewave.</summary>
        public int Spawned;
        public UnitFlags Flags;

        public UnitDef Def => UnitDef.Of(Type);

        public UnitSnapshot ToSnapshot() => new UnitSnapshot
        {
            Id = Id, Name = Name, Owner = Owner, Type = Type, X = X, Y = Y, Hp = Hp, MaxHp = Def.MaxHp,
            Order = Order, TargetX = TargetX, TargetY = TargetY, TargetId = TargetId, Flags = Flags,
        };
    }
}
