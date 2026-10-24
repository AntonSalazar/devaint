using System;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Видимость между точками: стены и закрытые двери загораживают, отсутствие пола - нет.</summary>
public static class Sight
{
    /// <summary>Свободна ли прямая между точками одного этажа.</summary>
    public static bool Clear(World world, WorldPos from, WorldPos to)
    {
        if (from.Z != to.Z)
        {
            return false;
        }

        // Проход по клеткам вдоль прямой: каждое пересечённое ребро проверяется на стену
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
            Side side = nextX < nextY ? (stepX > 0 ? Side.East : Side.West) : (stepY > 0 ? Side.South : Side.North);
            if (!world.GetEdge(cell, side).IsPassable)
            {
                return false;
            }

            cell = cell.Neighbor(side);
            if (nextX < nextY)
            {
                nextX += deltaX;
            }
            else
            {
                nextY += deltaY;
            }
        }

        return true;
    }
}
