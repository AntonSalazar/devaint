using System;
using System.Collections.Generic;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Поиск пути по клеткам этажа: A* с ходами по диагонали, которые не срезают углы стен.</summary>
public sealed class Pathfinder
{
    /// <summary>Наибольшее число раскрытых клеток за поиск, чтобы далёкая цель не съела тик.</summary>
    public const int MaxNodes = 4000;

    /// <summary>Цена хода по диагонали.</summary>
    private static readonly double _diagonal = Math.Sqrt(2);

    /// <summary>Восемь ходов: сначала прямые, потом диагональные.</summary>
    private static readonly (int Dx, int Dy)[] _steps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    // Коллекции поиска переиспользуются: поиск идёт каждый тик для многих существ,
    // замер на 1000 зомби показал 256 КБ мусора за тик при новых коллекциях на каждый поиск

    /// <summary>Клетки к раскрытию по оценке пути.</summary>
    private readonly PriorityQueue<CellPos, double> _open = new();

    /// <summary>Откуда пришли в клетку и цена пути до неё.</summary>
    private readonly Dictionary<CellPos, (CellPos Parent, double Cost)> _seen = [];

    /// <summary>Раскрытые клетки.</summary>
    private readonly HashSet<CellPos> _closed = [];

    /// <summary>Клетки пути от from до to без from; если цель не достигнута, путь ведёт к ближайшей к ней клетке, результат false.</summary>
    public bool Find(World world, CellPos from, CellPos to, List<CellPos> path)
    {
        path.Clear();
        if (from == to || from.Z != to.Z)
        {
            return from == to;
        }

        PriorityQueue<CellPos, double> open = _open;
        Dictionary<CellPos, (CellPos Parent, double Cost)> seen = _seen;
        HashSet<CellPos> closed = _closed;
        open.Clear();
        seen.Clear();
        closed.Clear();
        seen[from] = (from, 0);
        open.Enqueue(from, Estimate(from, to));
        CellPos best = from;
        double bestEstimate = Estimate(from, to);
        while (closed.Count < MaxNodes && open.TryDequeue(out CellPos cell, out _))
        {
            if (cell == to)
            {
                best = cell;
                break;
            }

            // В очереди бывают устаревшие записи той же клетки с худшей ценой
            if (!closed.Add(cell))
            {
                continue;
            }

            double cost = seen[cell].Cost;
            foreach ((int dx, int dy) in _steps)
            {
                if (!CanStep(world, cell, dx, dy))
                {
                    continue;
                }

                CellPos next = new(cell.X + dx, cell.Y + dy, cell.Z);
                double nextCost = cost + (dx != 0 && dy != 0 ? _diagonal : 1);
                if (closed.Contains(next) || (seen.TryGetValue(next, out (CellPos Parent, double Cost) known) && known.Cost <= nextCost))
                {
                    continue;
                }

                seen[next] = (cell, nextCost);
                double estimate = Estimate(next, to);
                open.Enqueue(next, nextCost + estimate);
                if (estimate < bestEstimate)
                {
                    (best, bestEstimate) = (next, estimate);
                }
            }
        }

        for (CellPos cell = best; cell != from; cell = seen[cell].Parent)
        {
            path.Add(cell);
        }

        path.Reverse();
        return best == to;
    }

    /// <summary>Можно ли сделать ход; по диагонали - только если свободны оба прямых обхода.</summary>
    private static bool CanStep(World world, CellPos cell, int dx, int dy)
    {
        Side? horizontal = dx > 0 ? Side.East : dx < 0 ? Side.West : null;
        Side? vertical = dy > 0 ? Side.South : dy < 0 ? Side.North : null;
        if (horizontal is not Side h)
        {
            return world.CanCross(cell, vertical!.Value);
        }

        if (vertical is not Side v)
        {
            return world.CanCross(cell, h);
        }

        return world.CanCross(cell, h) && world.CanCross(cell, v) && world.CanCross(cell.Neighbor(h), v) && world.CanCross(cell.Neighbor(v), h);
    }

    /// <summary>Оценка пути без стен: прямые ходы плюс диагональные.</summary>
    private static double Estimate(CellPos from, CellPos to)
    {
        int dx = Math.Abs(from.X - to.X);
        int dy = Math.Abs(from.Y - to.Y);
        return Math.Max(dx, dy) + ((_diagonal - 1) * Math.Min(dx, dy));
    }
}
