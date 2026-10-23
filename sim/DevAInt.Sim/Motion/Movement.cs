using System;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Motion;

/// <summary>Свободное движение круга по миру со столкновениями о непроходимые рёбра клеток.</summary>
public static class Movement
{
    /// <summary>Наибольшее число проходов выталкивания за шаг: в углу комнаты нужно два.</summary>
    private const int PushIterations = 4;

    /// <summary>Новая позиция после движения; чистая функция, мир не меняется.</summary>
    public static WorldPos Step(World world, WorldPos from, MoveInput input, double seconds, MoveParams body)
    {
        MoveInput direction = input.Clamped();
        double dx = direction.X * body.Speed * seconds;
        double dy = direction.Y * body.Speed * seconds;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance == 0)
        {
            return from;
        }

        // Шаги не длиннее половины радиуса: так тело не проскочит сквозь стену на большой скорости
        int steps = (int)Math.Ceiling(distance / (body.Radius / 2));
        double x = from.X;
        double y = from.Y;
        for (int i = 0; i < steps; i++)
        {
            x += dx / steps;
            y += dy / steps;
            (x, y) = PushOut(world, x, y, from.Z, body.Radius);
        }

        return new WorldPos(x, y, from.Z);
    }

    /// <summary>Выталкивание круга из всех непроходимых рёбер рядом; движение в стену пропадает, вдоль стены остаётся.</summary>
    private static (double X, double Y) PushOut(World world, double x, double y, int z, double radius)
    {
        for (int iteration = 0; iteration < PushIterations; iteration++)
        {
            bool pushed = false;

            // Клетки, которых касается круг; каждая отвечает за своё северное и западное ребро
            int minX = (int)Math.Floor(x - radius);
            int maxX = (int)Math.Floor(x + radius);
            int minY = (int)Math.Floor(y - radius);
            int maxY = (int)Math.Floor(y + radius);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    CellPos cell = new(cx, cy, z);
                    if (IsBlocked(world, cell, Side.North))
                    {
                        pushed |= PushFromSegment(ref x, ref y, cx, cy, cx + 1, cy, radius);
                    }

                    if (IsBlocked(world, cell, Side.West))
                    {
                        pushed |= PushFromSegment(ref x, ref y, cx, cy, cx, cy + 1, radius);
                    }
                }
            }

            if (!pushed)
            {
                break;
            }
        }

        return (x, y);
    }

    /// <summary>Нельзя пройти через ребро: стена, закрытая дверь или с какой-то стороны нет пола.</summary>
    private static bool IsBlocked(World world, CellPos cell, Side side)
    {
        bool wall = !world.GetEdge(cell, side).IsPassable;
        return wall || !world.IsWalkable(cell) || !world.IsWalkable(cell.Neighbor(side));
    }

    /// <summary>Выталкивание центра на радиус от ближайшей точки отрезка, true если было пересечение.</summary>
    private static bool PushFromSegment(ref double x, ref double y, double ax, double ay, double bx, double by, double radius)
    {
        // Отрезки рёбер всегда вдоль осей, поэтому ближайшая точка - зажатие координат
        double px = Math.Clamp(x, Math.Min(ax, bx), Math.Max(ax, bx));
        double py = Math.Clamp(y, Math.Min(ay, by), Math.Max(ay, by));
        double ox = x - px;
        double oy = y - py;
        double distance = Math.Sqrt((ox * ox) + (oy * oy));

        // Центр ровно на отрезке невозможен: шаг меньше радиуса, а до шага тело было снаружи
        if (distance >= radius || distance < 1e-12)
        {
            return false;
        }

        x = px + (ox / distance * radius);
        y = py + (oy / distance * radius);
        return true;
    }
}
