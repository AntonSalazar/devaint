using System.Collections.Generic;

namespace DevAInt.Sim.Grid;

/// <summary>Комнаты: клетки этажа, связанные без пересечения стен и дверей и окружённые ими со всех сторон.</summary>
public static class Rooms
{
    /// <summary>Наибольшая комната в клетках: больше - это уже улица или ангар.</summary>
    public const int MaxRoomCells = 1024;

    /// <summary>Сторона клетки по порядку обхода.</summary>
    private static readonly Side[] _sides = [Side.North, Side.East, Side.South, Side.West];

    /// <summary>Клетки комнаты, где стоит start; null, если клетка на улице: заливка вышла к краю пола без стены или комната слишком велика.</summary>
    public static HashSet<CellPos>? Find(World world, CellPos start, int maxCells = MaxRoomCells)
    {
        if (!world.IsWalkable(start))
        {
            return null;
        }

        HashSet<CellPos> room = [start];
        Queue<CellPos> open = new();
        open.Enqueue(start);
        while (open.TryDequeue(out CellPos cell))
        {
            foreach (Side side in _sides)
            {
                // Стена или дверь, открытая или закрытая, - граница комнаты
                if (world.GetEdge(cell, side).Wall != 0)
                {
                    continue;
                }

                // Проём над лестницей без пола - часть комнаты, а не выход на улицу
                CellPos next = cell.Neighbor(side);
                if (!world.IsWalkable(next) && !IsStairwell(world, next))
                {
                    return null;
                }

                if (room.Add(next))
                {
                    if (room.Count > maxCells)
                    {
                        return null;
                    }

                    open.Enqueue(next);
                }
            }
        }

        return room;
    }

    /// <summary>Клетка - проём над лестницей: под ней этажом ниже стоит лестница.</summary>
    private static bool IsStairwell(World world, CellPos cell) => world.ObjectAt(cell with { Z = cell.Z - 1 }) is { IsStairs: true };
}
