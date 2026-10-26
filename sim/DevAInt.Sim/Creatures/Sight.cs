using System;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Видимость между точками: стены, закрытые двери и высокие объекты загораживают, отсутствие пола - нет.</summary>
public static class Sight
{
    /// <summary>Свободна ли прямая между точками одного этажа.</summary>
    public static bool Clear(World world, WorldPos from, WorldPos to) => from.Z == to.Z && BlockedAt(world, from, to) < 0;

    /// <summary>Доля пути от from до to, на которой прямая упирается в стену, закрытую дверь или объект, закрывающий обзор; null - путь свободен.</summary>
    public static double? Blocked(World world, WorldPos from, WorldPos to) => BlockedAt(world, from, to) is double at and >= 0 ? at : null;

    /// <summary>Доля пути до первой стены или -1, если путь свободен.</summary>
    private static double BlockedAt(World world, WorldPos from, WorldPos to)
    {
        // Без double? в результате: Clear зовут тысячи раз на расчёт видимости, и обёртка стоила 4% времени
        // Проход по клеткам вдоль прямой: каждое пересечённое ребро проверяется на стену.
        // nextX и nextY - доли пути до следующего пересечения ребра по x и по y
        CellPos cell = from.Cell;
        CellPos end = to.Cell;
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        double deltaX = dx == 0 ? double.PositiveInfinity : 1 / Math.Abs(dx);
        double deltaY = dy == 0 ? double.PositiveInfinity : 1 / Math.Abs(dy);
        double nextX = dx == 0 ? double.PositiveInfinity : (stepX > 0 ? cell.X + 1 - from.X : from.X - cell.X) * deltaX;
        double nextY = dy == 0 ? double.PositiveInfinity : (stepY > 0 ? cell.Y + 1 - from.Y : from.Y - cell.Y) * deltaY;
        int steps = Math.Abs(end.X - cell.X) + Math.Abs(end.Y - cell.Y);
        for (int i = 0; i < steps; i++)
        {
            bool alongX = nextX < nextY;
            double at = alongX ? nextX : nextY;
            Side side = alongX ? (stepX > 0 ? Side.East : Side.West) : (stepY > 0 ? Side.South : Side.North);
            if (!world.GetEdge(cell, side).IsPassable)
            {
                return at;
            }

            // Объект в конечной клетке виден сам, а закрывает только то, что за ним
            cell = cell.Neighbor(side);
            if (cell != end && world.BlocksSight(cell))
            {
                return at;
            }

            if (alongX)
            {
                nextX += deltaX;
            }
            else
            {
                nextY += deltaY;
            }
        }

        return -1;
    }
}
