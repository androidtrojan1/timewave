using TimeWave.Shared;

namespace TimeWave.Core
{
    public static class Scenarios
    {
        /// <summary>Two mirrored bases: a factory that spawns soldiers, plus a starting squad of soldiers and tanks.</summary>
        public static WorldState Skirmish(ScenarioConfig config)
        {
            var world = new WorldState();
            int midY = config.MapHeight * World.TileSize / 2;
            for (byte owner = 0; owner < 2; owner++)
            {
                int side = owner == 0 ? 1 : -1;
                int baseX = owner == 0 ? 5 * World.TileSize : (config.MapWidth - 5) * World.TileSize;
                Add(world, owner + 1, owner, UnitType.Factory, baseX, midY);

                int firstId = 10 + owner * 20;
                for (int i = 0; i < 8; i++)
                    Add(world, firstId + i, owner, UnitType.Soldier, baseX + side * 4000 + (i / 4) * side * 1000, midY + (i % 4 - 1) * 1200 - 600);
                for (int i = 0; i < 2; i++)
                    Add(world, firstId + 8 + i, owner, UnitType.Tank, baseX + side * 2500, midY + (i == 0 ? -3500 : 3500));
            }
            return world;
        }

        static void Add(WorldState world, int id, byte owner, UnitType type, int x, int y)
        {
            world.Insert(new UnitState { Id = id, Name = id, Owner = owner, Type = type, X = x, Y = y, Hp = UnitDef.Of(type).MaxHp });
        }
    }
}
