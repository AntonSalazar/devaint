using System;
using System.Text;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Grid;

/// <summary>ASCII-дамп этажа мира для отладки и тестов.</summary>
public static class WorldDump
{
    // Сетка дампа вдвое подробнее клеток: нечётные x и y - клетки, чётные - рёбра и углы между ними.
    // Пол ".", стена "-" и "|", закрытая дверь "D", открытая "/", угол "+" там, где сходятся стены.

    /// <summary>Этаж z, обрезанный по занятым клеткам и рёбрам; пустая строка, если на этаже ничего нет.</summary>
    public static string Ascii(World world, WorldTable table, int z = 0)
    {
        int gridWidth = (2 * world.Width) + 1;
        int gridHeight = (2 * world.Height) + 1;
        char[,] grid = new char[gridWidth, gridHeight];
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;

        for (int gy = 0; gy < gridHeight; gy++)
        {
            for (int gx = 0; gx < gridWidth; gx++)
            {
                char symbol = Symbol(world, table, gx, gy, z);
                grid[gx, gy] = symbol;
                if (symbol != ' ')
                {
                    minX = Math.Min(minX, gx);
                    minY = Math.Min(minY, gy);
                    maxX = Math.Max(maxX, gx);
                    maxY = Math.Max(maxY, gy);
                }
            }
        }

        if (maxX < 0)
        {
            return string.Empty;
        }

        // Углы ставятся после рёбер, потому что зависят от соседних символов
        for (int gy = 0; gy < gridHeight; gy += 2)
        {
            for (int gx = 0; gx < gridWidth; gx += 2)
            {
                grid[gx, gy] = Corner(grid, gx, gy);
            }
        }

        StringBuilder text = new();
        for (int gy = minY; gy <= maxY; gy++)
        {
            StringBuilder line = new();
            for (int gx = minX; gx <= maxX; gx++)
            {
                line.Append(grid[gx, gy]);
            }

            text.Append(line.ToString().TrimEnd()).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Символ клетки или ребра; углы пока пробел.</summary>
    private static char Symbol(World world, WorldTable table, int gx, int gy, int z)
    {
        bool oddX = gx % 2 == 1;
        bool oddY = gy % 2 == 1;
        CellPos cell = new(gx / 2, gy / 2, z);
        if (oddX && oddY)
        {
            return world.IsWalkable(cell) ? '.' : ' ';
        }

        if (oddX == oddY)
        {
            return ' ';
        }

        // Рёбра читаются как северное или западное ребро клетки, которая их хранит
        Edge edge = oddX ? world.GetEdge(cell, Side.North) : world.GetEdge(cell, Side.West);
        if (edge.Wall == 0)
        {
            return ' ';
        }

        if (table.Wall(edge.Wall).Kind == WallKind.Door)
        {
            return edge.Open ? '/' : 'D';
        }

        return oddX ? '-' : '|';
    }

    /// <summary>Угол: "+" где сходятся стены разных направлений, иначе продолжение линии.</summary>
    private static char Corner(char[,] grid, int gx, int gy)
    {
        bool horizontal = IsWall(grid, gx - 1, gy) || IsWall(grid, gx + 1, gy);
        bool vertical = IsWall(grid, gx, gy - 1) || IsWall(grid, gx, gy + 1);
        return (horizontal, vertical) switch
        {
            (true, true) => '+',
            (true, false) => '-',
            (false, true) => '|',
            _ => ' ',
        };
    }

    /// <summary>Есть ли в позиции сетки стена или дверь.</summary>
    private static bool IsWall(char[,] grid, int gx, int gy)
    {
        bool inside = gx >= 0 && gy >= 0 && gx < grid.GetLength(0) && gy < grid.GetLength(1);
        return inside && grid[gx, gy] is '-' or '|' or 'D' or '/';
    }
}
