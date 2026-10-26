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

    /// <summary>Цель, ушедшая на соседний этаж не дальше этого по плоскости, не теряется: существо видело, как она пошла по лестнице.</summary>
    private const double FollowAcrossFloors = 3.5;

    /// <summary>Доля дальности атаки, на которой существо останавливается, чтобы не дрожать на границе.</summary>
    private const double StopFraction = 0.8;

    /// <summary>Решение на тик: ввод движения и атака, если цель на дистанции; candidates - сущности видов-целей.</summary>
    public static void Think(Simulation sim, Entity self, BrainDef brain, IReadOnlyList<Entity> candidates)
    {
        BrainState state = self.Brain!;
        long tick = sim.Clock.Tick;
        if (tick >= state.ScanTick || state.Target is not Entity current || !(Sees(sim.World, self, current, brain) || FollowsUpOrDown(self, current)))
        {
            // Поиск видит только свой этаж: цель, которая только что ушла по лестнице, остаётся прежней
            Entity? found = FindTarget(sim.World, self, brain, candidates);
            bool keep = found is null && state.Target is Entity old && FollowsUpOrDown(self, old);
            state.Target = keep ? state.Target : found;
            state.ScanTick = tick + ScanTicks;
        }

        if (state.Target is Entity target)
        {
            state.LastSeen = target.Position;
            double distance = Distance(self.Position, target.Position);
            AttackDef? attack = self.Creature?.Attack;
            if (attack is not null && distance <= attack.Range * StopFraction && self.Position.Z == target.Position.Z)
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

    /// <summary>Цель только что ушла на соседний этаж рядом с существом: оно идёт за ней по лестнице, а не теряет.</summary>
    private static bool FollowsUpOrDown(Entity self, Entity target) =>
        Math.Abs(self.Position.Z - target.Position.Z) == 1 && Distance(self.Position, target.Position) <= FollowAcrossFloors;

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

/// <summary>Атака и толчок: дальность, перезарядка, урон, отлёт и оглушение цели.</summary>
internal static class Combat
{
    /// <summary>За сколько секунд симуляции цель отлетает после толчка.</summary>
    private const double KnockSeconds = 0.2;

    /// <summary>Атака по цели, если атака готова, цель на дистанции и у неё есть нужный стат; true - удар прошёл.</summary>
    public static bool TryAttack(Simulation sim, Entity attacker, Entity target)
    {
        if (attacker.Creature?.Attack is not AttackDef attack || sim.Clock.Tick < attacker.AttackReadyTick || !InRange(attacker, target, attack.Range))
        {
            return false;
        }

        if (!sim.StatTable.TryStatIndex(attack.Stat, out int stat) || !target.Stats.IsActive(stat))
        {
            return false;
        }

        target.Stats.SetValue(stat, target.Stats.Value(stat) + attack.Add);
        sim.RecordAttack(attacker, target, AttackKind.Strike);
        attacker.AttackReadyTick = sim.Clock.Tick + Ticks(sim, attack.Seconds);
        return true;
    }

    /// <summary>Толчок цели: она отлетает от толкающего и оглушена; true - толчок прошёл.</summary>
    public static bool TryShove(Simulation sim, Entity shover, Entity target)
    {
        if (shover.Creature?.Shove is not ShoveDef shove || sim.Clock.Tick < shover.ShoveReadyTick || !InRange(shover, target, shove.Range))
        {
            return false;
        }

        // Отлёт от толкающего; если стоят в одной точке - туда, куда толкающий смотрит
        double dx = target.Position.X - shover.Position.X;
        double dy = target.Position.Y - shover.Position.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        (double x, double y) = length > 1e-6 ? (dx / length, dy / length) : shover.Facing;
        int knockTicks = Ticks(sim, KnockSeconds);
        target.Knock = (x, y, shove.Push / (knockTicks * sim.Clock.SecondsPerTick), sim.Clock.Tick + knockTicks);
        target.StunnedUntilTick = Math.Max(target.StunnedUntilTick, sim.Clock.Tick + Ticks(sim, shove.Stun));
        shover.ShoveReadyTick = sim.Clock.Tick + Ticks(sim, shove.Seconds);
        sim.RecordAttack(shover, target, AttackKind.Shove);
        return true;
    }

    /// <summary>Ближайшая сущность впереди на дистанции range, подходящая под accept, или null.</summary>
    public static Entity? FrontTarget(Simulation sim, Entity attacker, double range, Func<Entity, bool> accept)
    {
        Entity? best = null;
        double bestDistance = double.MaxValue;
        foreach (Entity other in sim.Entities)
        {
            double dx = other.Position.X - attacker.Position.X;
            double dy = other.Position.Y - attacker.Position.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            // Впереди - в полуплоскости взгляда: сзади удар не достаёт
            bool ahead = (dx * attacker.Facing.X) + (dy * attacker.Facing.Y) >= 0;
            if (other != attacker && ahead && distance < bestDistance && InRange(attacker, other, range) && accept(other))
            {
                (best, bestDistance) = (other, distance);
            }
        }

        return best;
    }

    /// <summary>Ближайшая цель удара впереди: на дистанции атаки и со статом атаки, или null.</summary>
    public static Entity? StrikeTarget(Simulation sim, Entity attacker)
    {
        if (attacker.Creature?.Attack is not AttackDef attack || !sim.StatTable.TryStatIndex(attack.Stat, out int stat))
        {
            return null;
        }

        return FrontTarget(sim, attacker, attack.Range, other => other.Stats.IsActive(stat));
    }

    /// <summary>Тиков на seconds секунд симуляции, не меньше одного.</summary>
    private static int Ticks(Simulation sim, double seconds) => Math.Max(1, (int)Math.Ceiling(seconds / sim.Clock.SecondsPerTick));

    /// <summary>Цель на том же этаже и на дистанции range от центра до центра.</summary>
    private static bool InRange(Entity attacker, Entity target, double range)
    {
        double dx = target.Position.X - attacker.Position.X;
        double dy = target.Position.Y - attacker.Position.Y;
        return attacker.Position.Z == target.Position.Z && (dx * dx) + (dy * dy) <= range * range;
    }
}
