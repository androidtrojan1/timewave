using System.Collections.Generic;
using TimeWave.Shared;

namespace TimeWave.Core
{
    /// <summary>
    /// Advances a world by exactly one tick. Pure and deterministic: the same state and commands
    /// always give the same result, which is what lets timewaves re-simulate the past at will.
    /// All decisions read the state from the start of the tick and are applied together.
    /// </summary>
    public static class Simulation
    {
        public static void Step(WorldState world, IReadOnlyList<Command> commands, ScenarioConfig config, TickStats stats)
        {
            var units = world.Units;
            if (commands != null)
                for (int i = 0; i < commands.Count; i++) Apply(world, commands[i]);

            int n = units.Count;
            var newX = new int[n];
            var newY = new int[n];
            var damage = new int[n];
            List<UnitState> spawned = null;

            for (int i = 0; i < n; i++)
            {
                var u = units[i];
                var def = u.Def;
                newX[i] = u.X;
                newY[i] = u.Y;
                u.Flags = UnitFlags.None;

                if (u.Type == UnitType.Factory)
                {
                    if (++u.Timer >= def.SpawnTicks)
                    {
                        u.Timer = 0;
                        u.Spawned++;
                        int side = u.Owner == 0 ? 1 : -1;
                        var soldier = new UnitState
                        {
                            Id = u.Id * 1000 + u.Spawned,
                            Owner = u.Owner,
                            Type = UnitType.Soldier,
                            X = u.X + side * 2000,
                            Y = u.Y + (u.Spawned % 3 - 1) * 900,
                            Hp = UnitDef.Of(UnitType.Soldier).MaxHp,
                        };
                        soldier.Name = soldier.Id;
                        (spawned ??= new List<UnitState>()).Add(soldier);
                        stats.Created[u.Owner]++;
                    }
                    units[i] = u;
                    continue;
                }

                if (u.Cooldown > 0) u.Cooldown--;

                int target = -1;
                if (u.Order == OrderType.Attack)
                {
                    target = world.IndexOf(u.TargetId);
                    if (target < 0) { u.Order = OrderType.None; u.TargetId = 0; }
                }
                if (target < 0 && (u.Order == OrderType.None || u.Order == OrderType.AttackMove))
                    target = NearestEnemy(units, i, def.AcquireRange);

                if (target >= 0)
                {
                    var t = units[target];
                    long reach = def.Range + t.Def.Radius;
                    if (IntMath.DistSq(u.X, u.Y, t.X, t.Y) <= reach * reach)
                    {
                        if (u.Cooldown == 0)
                        {
                            damage[target] += def.Damage;
                            stats.DamageDealt[u.Owner] += def.Damage;
                            stats.DamageTaken[t.Owner] += def.Damage;
                            u.Cooldown = def.Cooldown;
                        }
                        u.Flags |= UnitFlags.Firing;
                    }
                    else
                    {
                        MoveToward(ref newX[i], ref newY[i], t.X, t.Y, def.Speed);
                        u.Flags |= UnitFlags.Moving;
                    }
                }
                else if (u.Order == OrderType.Move || u.Order == OrderType.AttackMove)
                {
                    if (MoveToward(ref newX[i], ref newY[i], u.TargetX, u.TargetY, def.Speed))
                        u.Order = OrderType.None;
                    else
                        u.Flags |= UnitFlags.Moving;
                }
                units[i] = u;
            }

            int maxX = config.MapWidth * World.TileSize, maxY = config.MapHeight * World.TileSize;
            for (int i = 0; i < n; i++)
            {
                var u = units[i];
                u.X = IntMath.Clamp(newX[i], 0, maxX);
                u.Y = IntMath.Clamp(newY[i], 0, maxY);
                u.Hp = (short)(u.Hp - damage[i]);
                units[i] = u;
            }
            units.RemoveAll(u => u.Hp <= 0);

            Separate(units);

            if (spawned != null)
                foreach (var s in spawned) world.Insert(s);

            foreach (var u in units) stats.UnitCount[u.Owner]++;
            world.Tick++;
        }

        static void Apply(WorldState world, Command c)
        {
            if (c.UnitIds == null) return;
            foreach (var id in c.UnitIds)
            {
                int i = world.IndexOf(id);
                if (i < 0) continue;
                var u = world.Units[i];
                // Validated against the world at that time: a unit you do not own (or that does not exist) on this timeline ignores the order.
                if (u.Owner != c.Player || u.Type == UnitType.Factory) continue;
                switch (c.Order)
                {
                    case OrderType.Stop:
                        u.Order = OrderType.None; u.TargetId = 0;
                        break;
                    case OrderType.Move:
                    case OrderType.AttackMove:
                        u.Order = c.Order; u.TargetX = c.TargetX; u.TargetY = c.TargetY; u.TargetId = 0;
                        break;
                    case OrderType.Attack:
                        u.Order = OrderType.Attack; u.TargetId = c.TargetId;
                        break;
                }
                world.Units[i] = u;
            }
        }

        static int NearestEnemy(List<UnitState> units, int self, int range)
        {
            var u = units[self];
            long best = (long)range * range;
            int bestIndex = -1;
            for (int j = 0; j < units.Count; j++)
            {
                var o = units[j];
                if (o.Owner == u.Owner) continue;
                long d = IntMath.DistSq(u.X, u.Y, o.X, o.Y);
                if (d < best) { best = d; bestIndex = j; }
            }
            return bestIndex;
        }

        /// <summary>Returns true when the destination was reached this tick.</summary>
        static bool MoveToward(ref int x, ref int y, int tx, int ty, int speed)
        {
            if (speed <= 0) return false;
            long dx = tx - x, dy = ty - y;
            int dist = IntMath.Sqrt(dx * dx + dy * dy);
            if (dist <= speed) { x = tx; y = ty; return true; }
            x += (int)(dx * speed / dist);
            y += (int)(dy * speed / dist);
            return false;
        }

        /// <summary>Pushes overlapping mobile units apart, in a fixed pair order.</summary>
        static void Separate(List<UnitState> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var a = units[i];
                if (a.Type == UnitType.Factory) continue;
                for (int j = i + 1; j < units.Count; j++)
                {
                    var b = units[j];
                    if (b.Type == UnitType.Factory) continue;
                    int min = a.Def.Radius + b.Def.Radius;
                    long dx = b.X - a.X, dy = b.Y - a.Y;
                    long dsq = dx * dx + dy * dy;
                    if (dsq >= (long)min * min) continue;
                    int dist = IntMath.Sqrt(dsq);
                    int push = (min - dist) / 2 + 1;
                    int px, py;
                    if (dist == 0) { px = ((a.Id + b.Id) & 1) == 0 ? push : 0; py = px == 0 ? push : 0; }
                    else { px = (int)(dx * push / dist); py = (int)(dy * push / dist); }
                    a.X -= px; a.Y -= py;
                    b.X += px; b.Y += py;
                    units[j] = b;
                }
                units[i] = a;
            }
        }
    }
}
