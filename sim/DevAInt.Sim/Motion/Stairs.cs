using System;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Motion;

/// <summary>Лестницы между этажами: верхний край лестницы ведёт на этаж выше, над лестницей наверху - проём без пола.</summary>
public static class Stairs
{
    /// <summary>Сторона, куда лестница поднимается: без поворота на север, каждый поворот - на четверть оборота по часовой.</summary>
    public static Side Up(WorldObject stairs) => stairs.Rotation switch
    {
        0 => Side.North,
        1 => Side.East,
        2 => Side.South,
        _ => Side.West,
    };

    /// <summary>Верхняя клетка лестницы: из неё шаг в сторону Up выводит на этаж выше.</summary>
    public static CellPos Top(WorldObject stairs) => Up(stairs) switch
    {
        Side.East => stairs.Cell with { X = stairs.Cell.X + stairs.Width - 1 },
        Side.South => stairs.Cell with { Y = stairs.Cell.Y + stairs.Depth - 1 },
        _ => stairs.Cell,
    };

    /// <summary>Подъём в точке: доля этажа от 0 внизу лестницы до 1 у верхнего края; не на лестнице - 0. Нужен картинке и камере.</summary>
    public static double Rise(World world, WorldPos point)
    {
        if (world.ObjectAt(point.Cell) is not { IsStairs: true } stairs)
        {
            return 0;
        }

        // Путь от нижнего края лестницы вдоль подъёма, делённый на её длину
        double along = Up(stairs) switch
        {
            Side.North => stairs.Cell.Y + stairs.Depth - point.Y,
            Side.South => point.Y - stairs.Cell.Y,
            Side.East => point.X - stairs.Cell.X,
            _ => stairs.Cell.X + stairs.Width - point.X,
        };
        return Math.Clamp(along / Math.Max(stairs.Width, stairs.Depth), 0, 1);
    }

    /// <summary>Ребро клетки cell со стороны side - верхний край лестницы на этаже cell или этажом ниже: клетка выхода на этаже над лестницей, иначе null.</summary>
    public static CellPos? TopEdgeExit(World world, CellPos cell, Side side)
    {
        if (!world.NearStairs(cell, cell.Neighbor(side)))
        {
            return null;
        }

        // Край виден с двух этажей: снизу это выход с лестницы, сверху - край проёма над ней
        for (int dz = 0; dz >= -1; dz--)
        {
            CellPos a = cell with { Z = cell.Z + dz };
            CellPos b = a.Neighbor(side);
            if (IsTopFacing(world, a, side))
            {
                return b with { Z = b.Z + 1 };
            }

            if (IsTopFacing(world, b, Opposite(side)))
            {
                return a with { Z = a.Z + 1 };
            }
        }

        return null;
    }

    /// <summary>На сколько этажей меняется высота при шаге из клетки from в соседнюю to: +1 с верха лестницы на выход, -1 с выхода в проём над лестницей, иначе 0.</summary>
    public static int LevelChange(World world, CellPos from, CellPos to)
    {
        if (SideBetween(from, to) is not Side side || !world.NearStairs(from, to))
        {
            return 0;
        }

        if (IsTopFacing(world, from, side))
        {
            return 1;
        }

        return IsTopFacing(world, to with { Z = to.Z - 1 }, Opposite(side)) ? -1 : 0;
    }

    /// <summary>Клетка, в которую ведёт лестница из cell: с верха лестницы - выход этажом выше, с выхода - верх лестницы этажом ниже; null - лестницы нет.</summary>
    public static CellPos? Across(World world, CellPos cell, Side side)
    {
        CellPos next = cell.Neighbor(side);
        return LevelChange(world, cell, next) switch
        {
            1 => next with { Z = next.Z + 1 },
            -1 => next with { Z = next.Z - 1 },
            _ => null,
        };
    }

    /// <summary>Противоположная сторона.</summary>
    public static Side Opposite(Side side) => side switch
    {
        Side.North => Side.South,
        Side.South => Side.North,
        Side.East => Side.West,
        _ => Side.East,
    };

    /// <summary>Клетка - верх лестницы, поднимающейся в сторону side.</summary>
    private static bool IsTopFacing(World world, CellPos cell, Side side) =>
        world.ObjectAt(cell) is { IsStairs: true } stairs && Top(stairs) == cell && Up(stairs) == side;

    /// <summary>Сторона, через которую соседняя по ребру клетка to лежит от from; null - клетки не соседи по ребру.</summary>
    private static Side? SideBetween(CellPos from, CellPos to)
    {
        return (to.X - from.X, to.Y - from.Y) switch
        {
            (0, -1) => Side.North,
            (1, 0) => Side.East,
            (0, 1) => Side.South,
            (-1, 0) => Side.West,
            _ => null,
        };
    }
}
