using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;

namespace DevAInt.Sim.Creatures;

/// <summary>Поведение "заметить, догнать, атаковать" по параметрам BrainDef из мода.</summary>
internal static class Brain
{
    /// <summary>Поиск цели раз в полсекунды реального времени.</summary>
    private const int ScanTicks = 5;

    /// <summary>Путь строится заново раз в секунду, даже если цель стоит в той же клетке.</summary>
    private const int RepathTicks = 10;

    /// <summary>Расстояние до центра клетки пути, на котором она считается пройденной.</summary>
    private const double WaypointReach = 0.15;

    /// <summary>Доля дальности атаки, на которой существо останавливается, чтобы не дрожать на границе.</summary>
    private const double StopFraction = 0.8;

    /// <summary>Решение на тик: ввод движения и атака, если цель на дистанции; candidates - сущности видов-целей.</summary>
    public static void Think(Simulation sim, Entity self, BrainDef brain, IReadOnlyList<Entity> candidates)
    {
        BrainState state = self.Brain!;
        long tick = sim.Clock.Tick;
        if (tick >= state.ScanTick || state.Target is not Entity current || !Sees(sim.World, self, current, brain))
        {
            state.Target = FindTarget(sim.World, self, brain, candidates);
            state.ScanTick = tick + ScanTicks;
        }

        if (state.Target is Entity target)
        {
            state.LastSeen = target.Position;
            double distance = Distance(self.Position, target.Position);
            AttackDef? attack = self.Creature?.Attack;
            if (attack is not null && distance <= attack.Range * StopFraction)
            {
                self.Input = MoveInput.None;
                Combat.TryAttack(sim, self, target);
                return;
            }
        }

        if (state.LastSeen is not WorldPos goal)
        {
            self.Input = MoveInput.None;
            return;
        }

        // Цель пропала из виду, а место, где её видели, уже рядом: искать больше негде
        if (state.Target is null && Distance(self.Position, goal) < WaypointReach)
        {
            state.LastSeen = null;
            self.Input = MoveInput.None;
            return;
        }

        self.Input = Steer(sim, self, state, goal);
    }

    /// <summary>Ближайшая видимая цель в радиусе зрения или null.</summary>
    private static Entity? FindTarget(World world, Entity self, BrainDef brain, IReadOnlyList<Entity> candidates)
    {
        Entity? best = null;
        double bestDistance = double.MaxValue;
        foreach (Entity candidate in candidates)
        {
            double distance = Distance(self.Position, candidate.Position);
            if (candidate != self && distance < bestDistance && Sees(world, self, candidate, brain))
            {
                (best, bestDistance) = (candidate, distance);
            }
        }

        return best;
    }

    /// <summary>Видна ли сущность: тот же этаж, в радиусе зрения и без стен между ними.</summary>
    private static bool Sees(World world, Entity self, Entity other, BrainDef brain) => self.Position.Z == other.Position.Z && Distance(self.Position, other.Position) <= brain.Sight && Sight.Clear(world, self.Position, other.Position);

    /// <summary>Направление к следующей клетке пути до goal; в клетке цели - прямо к ней.</summary>
    private static MoveInput Steer(Simulation sim, Entity self, BrainState state, WorldPos goal)
    {
        CellPos here = self.Position.Cell;
        if (here == goal.Cell)
        {
            return Toward(self.Position, goal);
        }

        if (state.PathGoal != goal.Cell || sim.Clock.Tick >= state.RepathTick)
        {
            sim.Paths.Find(sim.World, here, goal.Cell, state.Path);
            state.PathIndex = 0;
            state.PathGoal = goal.Cell;
            state.RepathTick = sim.Clock.Tick + RepathTicks;
        }

        while (state.PathIndex < state.Path.Count && Distance(self.Position, WorldPos.CenterOf(state.Path[state.PathIndex])) < WaypointReach)
        {
            state.PathIndex++;
        }

        // Пути нет: цель за закрытой дверью или вне досягаемости, существо ждёт
        return state.PathIndex < state.Path.Count ? Toward(self.Position, WorldPos.CenterOf(state.Path[state.PathIndex])) : MoveInput.None;
    }

    /// <summary>Единичное направление от точки к точке, ноль если они совпали.</summary>
    private static MoveInput Toward(WorldPos from, WorldPos to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        return length < 1e-9 ? MoveInput.None : new MoveInput(dx / length, dy / length);
    }

    /// <summary>Расстояние по плоскости.</summary>
    private static double Distance(WorldPos a, WorldPos b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}

/// <summary>Атака: дальность, перезарядка и изменение стата цели.</summary>
internal static class Combat
{
    /// <summary>Атака по цели, если атака готова, цель на дистанции и у неё есть нужный стат; true - удар прошёл.</summary>
    public static bool TryAttack(Simulation sim, Entity attacker, Entity target)
    {
        if (attacker.Creature?.Attack is not AttackDef attack || sim.Clock.Tick < attacker.AttackReadyTick || !InRange(attacker, target, attack))
        {
            return false;
        }

        if (!sim.StatTable.TryStatIndex(attack.Stat, out int stat) || !target.Stats.IsActive(stat))
        {
            return false;
        }

        target.Stats.SetValue(stat, target.Stats.Value(stat) + attack.Add);
        sim.RecordAttack(attacker, target);
        attacker.AttackReadyTick = sim.Clock.Tick + Math.Max(1, (int)Math.Ceiling(attack.Seconds / sim.Clock.SecondsPerTick));
        return true;
    }

    /// <summary>Ближайшая сущность впереди на дистанции атаки, у которой есть стат атаки, или null.</summary>
    public static Entity? FrontTarget(Simulation sim, Entity attacker)
    {
        if (attacker.Creature?.Attack is not AttackDef attack || !sim.StatTable.TryStatIndex(attack.Stat, out int stat))
        {
            return null;
        }

        Entity? best = null;
        double bestDistance = double.MaxValue;
        foreach (Entity other in sim.Entities)
        {
            double dx = other.Position.X - attacker.Position.X;
            double dy = other.Position.Y - attacker.Position.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            // Впереди - в полуплоскости взгляда: сзади удар не достаёт
            bool ahead = (dx * attacker.Facing.X) + (dy * attacker.Facing.Y) >= 0;
            if (other != attacker && ahead && distance < bestDistance && InRange(attacker, other, attack) && other.Stats.IsActive(stat))
            {
                (best, bestDistance) = (other, distance);
            }
        }

        return best;
    }

    /// <summary>Цель на том же этаже и на дистанции атаки от центра до центра.</summary>
    private static bool InRange(Entity attacker, Entity target, AttackDef attack)
    {
        double dx = target.Position.X - attacker.Position.X;
        double dy = target.Position.Y - attacker.Position.Y;
        return attacker.Position.Z == target.Position.Z && (dx * dx) + (dy * dy) <= attack.Range * attack.Range;
    }
}
