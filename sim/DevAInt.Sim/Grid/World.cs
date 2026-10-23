using System;

namespace DevAInt.Sim.Grid;

/// <summary>Сетка мира из чанков: полы в клетках, стены и двери на рёбрах.</summary>
public sealed class World
{
    /// <summary>Сторона чанка в клетках.</summary>
    public const int ChunkSize = 32;

    /// <summary>Нижний этаж, подвалы.</summary>
    public const int MinLevel = -2;

    /// <summary>Верхний этаж.</summary>
    public const int MaxLevel = 8;

    /// <summary>Число этажей.</summary>
    public const int LevelCount = MaxLevel - MinLevel + 1;

    /// <summary>Наибольшая сторона карты в клетках.</summary>
    public const int MaxSize = 4096;

    /// <summary>Чанки построчно; хранение на клетку шире карты, чтобы у крайних клеток были свои восточные и южные рёбра.</summary>
    private readonly Chunk[] _chunks;

    /// <summary>Число чанков по x в хранении.</summary>
    private readonly int _chunksX;

    /// <summary>Пустой мир размером width на height клеток.</summary>
    public World(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxSize || height > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"world size {width}x{height} must be within 1..{MaxSize}");
        }

        Width = width;
        Height = height;
        _chunksX = (width / ChunkSize) + 1;
        int chunksY = (height / ChunkSize) + 1;
        _chunks = new Chunk[_chunksX * chunksY];
        for (int i = 0; i < _chunks.Length; i++)
        {
            _chunks[i] = new Chunk();
        }
    }

    /// <summary>Ширина в клетках.</summary>
    public int Width { get; }

    /// <summary>Высота в клетках.</summary>
    public int Height { get; }

    /// <summary>Число созданных слоев этажей во всех чанках, для проверки расхода памяти.</summary>
    internal int LevelsCreated
    {
        get
        {
            int count = 0;
            foreach (Chunk chunk in _chunks)
            {
                count += chunk.LevelsCreated;
            }

            return count;
        }
    }

    /// <summary>Лежит ли клетка внутри карты и этажей.</summary>
    public bool Contains(CellPos cell) => InRange(cell, Width, Height);

    /// <summary>Номер пола клетки, 0 - пола нет или клетка вне карты.</summary>
    public ushort Floor(CellPos cell)
    {
        if (!Contains(cell) || Level(cell) is not ChunkLevel level)
        {
            return 0;
        }

        return level.Floors[LocalIndex(cell)];
    }

    /// <summary>Установка пола клетки внутри карты.</summary>
    public void SetFloor(CellPos cell, ushort floor)
    {
        Require(Contains(cell), cell);
        GetOrCreateLevel(cell).Floors[LocalIndex(cell)] = floor;
    }

    /// <summary>Можно ли стоять на клетке: она в карте и на ней есть пол.</summary>
    public bool IsWalkable(CellPos cell) => Floor(cell) != 0;

    /// <summary>Ребро клетки со стороны side.</summary>
    public Edge GetEdge(CellPos cell, Side side)
    {
        (CellPos owner, bool north) = Owner(cell, side);
        if (!InStorage(owner) || Level(owner) is not ChunkLevel level)
        {
            return Edge.None;
        }

        int index = LocalIndex(owner);
        return north
            ? new Edge(level.NorthWalls[index], (level.Flags[index] & ChunkLevel.NorthOpen) != 0)
            : new Edge(level.WestWalls[index], (level.Flags[index] & ChunkLevel.WestOpen) != 0);
    }

    /// <summary>Установка ребра клетки внутри карты; у соседа это же ребро меняется автоматически.</summary>
    public void SetEdge(CellPos cell, Side side, Edge edge)
    {
        Require(Contains(cell), cell);
        (CellPos owner, bool north) = Owner(cell, side);
        ChunkLevel level = GetOrCreateLevel(owner);
        int index = LocalIndex(owner);
        byte flag = north ? ChunkLevel.NorthOpen : ChunkLevel.WestOpen;
        (north ? level.NorthWalls : level.WestWalls)[index] = edge.Wall;
        level.Flags[index] = edge.Open ? (byte)(level.Flags[index] | flag) : (byte)(level.Flags[index] & ~flag);
    }

    /// <summary>Можно ли перейти из клетки в соседнюю: сосед в карте, ребро проходимо, у соседа есть пол.</summary>
    public bool CanCross(CellPos cell, Side side) => GetEdge(cell, side).IsPassable && IsWalkable(cell.Neighbor(side));

    /// <summary>Клетка, которая хранит ребро, и true, если это её северное ребро.</summary>
    private static (CellPos Owner, bool North) Owner(CellPos cell, Side side)
    {
        // Каждая клетка хранит северное и западное ребро; южное и восточное - это рёбра соседа
        return side switch
        {
            Side.North => (cell, true),
            Side.South => (cell.Neighbor(Side.South), true),
            Side.West => (cell, false),
            _ => (cell.Neighbor(Side.East), false),
        };
    }

    /// <summary>Номер клетки внутри чанка.</summary>
    private static int LocalIndex(CellPos cell) => (cell.Y % ChunkSize * ChunkSize) + (cell.X % ChunkSize);

    /// <summary>Клетка в пределах width на height и этажей.</summary>
    private static bool InRange(CellPos cell, int width, int height)
    {
        bool flat = cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < height;
        return flat && cell.Z >= MinLevel && cell.Z <= MaxLevel;
    }

    /// <summary>Исключение для клетки вне карты.</summary>
    private static void Require(bool condition, CellPos cell)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(nameof(cell), $"cell {cell} is outside the world");
        }
    }

    /// <summary>Есть ли место хранения клетки: карта плюс одна клетка на восток и на юг.</summary>
    private bool InStorage(CellPos cell) => InRange(cell, Width + 1, Height + 1);

    /// <summary>Чанк клетки из хранения.</summary>
    private Chunk ChunkOf(CellPos cell) => _chunks[(cell.Y / ChunkSize * _chunksX) + (cell.X / ChunkSize)];

    /// <summary>Слой этажа клетки или null.</summary>
    private ChunkLevel? Level(CellPos cell) => ChunkOf(cell).Level(cell.Z);

    /// <summary>Слой этажа клетки, создаётся при необходимости.</summary>
    private ChunkLevel GetOrCreateLevel(CellPos cell) => ChunkOf(cell).GetOrCreateLevel(cell.Z);
}
