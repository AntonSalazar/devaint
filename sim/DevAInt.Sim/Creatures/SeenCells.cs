using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Память тумана войны: какие точки клеток персонаж уже видел, по чанкам и этажам.</summary>
public sealed class SeenCells
{
    /// <summary>Точек памяти вдоль стороны клетки: сетка 4 на 4, чтобы граница разведанного шла ровно.</summary>
    public const int PointsPerSide = 4;

    /// <summary>Биты всех точек клетки.</summary>
    public const ushort AllPoints = ushort.MaxValue;

    /// <summary>Клеток в слое чанка.</summary>
    private const int ChunkCells = World.ChunkSize * World.ChunkSize;

    /// <summary>Биты увиденных точек по клеткам слоёв чанков; слой создаётся при первой увиденной клетке.</summary>
    private readonly Dictionary<(int X, int Y, int Z), ushort[]> _chunks = [];

    /// <summary>Биты увиденных точек клетки: бит y * 4 + x, x на восток, y на юг.</summary>
    public ushort Points(CellPos cell) => cell.X >= 0 && cell.Y >= 0 && _chunks.TryGetValue(Key(cell), out ushort[]? points) ? points[Index(cell)] : (ushort)0;

    /// <summary>Видел ли персонаж хоть часть клетки.</summary>
    public bool IsSeen(CellPos cell) => Points(cell) != 0;

    /// <summary>Отметка точек, которые видны глазам eye при взгляде facing; карта map уже посчитана для них же.</summary>
    public void Mark(World world, WorldPos eye, (double X, double Y) facing, VisionDef vision, VisibilityMap map)
    {
        double halfCos = Vision.HalfCos(vision);
        for (int dy = -map.Radius; dy <= map.Radius; dy++)
        {
            for (int dx = -map.Radius; dx <= map.Radius; dx++)
            {
                CellPos cell = new(map.Center.X + dx, map.Center.Y + dy, map.Center.Z);
                float coverage = map.Coverage(cell);
                if (cell.X < 0 || cell.Y < 0 || coverage <= 0)
                {
                    continue;
                }

                // Лучи по точкам только для клеток на краю видимого: видная целиком отмечается сразу.
                // Угол клетки, который край конуса срезал мимо всех пяти точек расчёта, не отмечается -
                // это меньше пятой части клетки, а проверка соседей стоила бы 2 мс на пересчёт
                ushort points = coverage >= 1 ? AllPoints : VisiblePoints(world, eye, facing, vision, halfCos, cell);
                if (points != 0)
                {
                    Layer(cell)[Index(cell)] |= points;
                }
            }
        }
    }

    /// <summary>Биты точек клетки, которые видят глаза.</summary>
    private static ushort VisiblePoints(World world, WorldPos eye, (double X, double Y) facing, VisionDef vision, double halfCos, CellPos cell)
    {
        int points = 0;
        for (int y = 0; y < PointsPerSide; y++)
        {
            for (int x = 0; x < PointsPerSide; x++)
            {
                // Точка в центре своей доли клетки, как центр тексела памяти на клиенте
                WorldPos point = new(cell.X + ((x + 0.5) / PointsPerSide), cell.Y + ((y + 0.5) / PointsPerSide), cell.Z);
                if (Vision.Sees(world, eye, point, facing, vision, halfCos))
                {
                    points |= 1 << ((y * PointsPerSide) + x);
                }
            }
        }

        return (ushort)points;
    }

    /// <summary>Ключ слоя чанка клетки.</summary>
    private static (int X, int Y, int Z) Key(CellPos cell) => (cell.X / World.ChunkSize, cell.Y / World.ChunkSize, cell.Z);

    /// <summary>Номер клетки внутри слоя чанка.</summary>
    private static int Index(CellPos cell) => (cell.Y % World.ChunkSize * World.ChunkSize) + (cell.X % World.ChunkSize);

    /// <summary>Слой чанка клетки, создаётся при необходимости.</summary>
    private ushort[] Layer(CellPos cell)
    {
        if (!_chunks.TryGetValue(Key(cell), out ushort[]? points))
        {
            _chunks[Key(cell)] = points = new ushort[ChunkCells];
        }

        return points;
    }
}
