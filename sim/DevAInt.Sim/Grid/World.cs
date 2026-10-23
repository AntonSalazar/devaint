using System;
using System.Collections.Generic;

namespace DevAInt.Sim.Grid;

/// <summary>Что изменилось в мире: пол клетки или её северное или западное ребро.</summary>
internal enum WorldChangeKind
{
    /// <summary>Пол клетки.</summary>
    Floor,

    /// <summary>Северное ребро клетки.</summary>
    NorthEdge,

    /// <summary>Западное ребро клетки.</summary>
    WestEdge,
}

/// <summary>Изменение мира: клетка-владелец и что именно поменялось.</summary>
internal readonly record struct WorldChange(CellPos Owner, WorldChangeKind Kind);

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

    /// <summary>Изменения полов и рёбер с последней очистки, для рассылки клиентам.</summary>
    private readonly List<WorldChange> _changes = [];

    /// <summary>Пустой мир размером width на height клеток.</summary>
    public World(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxSize || height > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"world size {width}x{height} must be within 1..{MaxSize}");
        }

        Width = width;
        Height = height;
        ChunksX = (width / ChunkSize) + 1;
        ChunksY = (height / ChunkSize) + 1;
        _chunks = new Chunk[ChunksX * ChunksY];
        for (int i = 0; i < _chunks.Length; i++)
        {
            _chunks[i] = new Chunk();
        }
    }

    /// <summary>Ширина в клетках.</summary>
    public int Width { get; }

    /// <summary>Высота в клетках.</summary>
    public int Height { get; }

    /// <summary>Число чанков по x в хранении, с запасом под внешние рёбра.</summary>
    internal int ChunksX { get; }

    /// <summary>Число чанков по y в хранении.</summary>
    internal int ChunksY { get; }

    /// <summary>Изменения полов и рёбер с последней очистки.</summary>
    internal IReadOnlyList<WorldChange> Changes => _changes;

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
        _changes.Add(new WorldChange(cell, WorldChangeKind.Floor));
    }

    /// <summary>Можно ли стоять на клетке: она в карте и на ней есть пол.</summary>
    public bool IsWalkable(CellPos cell) => Floor(cell) != 0;

    /// <summary>Ребро клетки со стороны side.</summary>
    public Edge GetEdge(CellPos cell, Side side)
    {
        (CellPos owner, bool north) = Owner(cell, side);
        return GetOwnedEdge(owner, north);
    }

    /// <summary>Установка ребра клетки внутри карты; у соседа это же ребро меняется автоматически.</summary>
    public void SetEdge(CellPos cell, Side side, Edge edge)
    {
        Require(Contains(cell), cell);
        (CellPos owner, bool north) = Owner(cell, side);
        SetOwnedEdge(owner, north, edge);
    }

    /// <summary>Можно ли перейти из клетки в соседнюю: сосед в карте, ребро проходимо, у соседа есть пол.</summary>
    public bool CanCross(CellPos cell, Side side) => GetEdge(cell, side).IsPassable && IsWalkable(cell.Neighbor(side));

    /// <summary>Северное или западное ребро клетки-владельца из хранения.</summary>
    internal Edge GetOwnedEdge(CellPos owner, bool north)
    {
        if (!InStorage(owner) || Level(owner) is not ChunkLevel level)
        {
            return Edge.None;
        }

        int index = LocalIndex(owner);
        return north
            ? new Edge(level.NorthWalls[index], (level.Flags[index] & ChunkLevel.NorthOpen) != 0)
            : new Edge(level.WestWalls[index], (level.Flags[index] & ChunkLevel.WestOpen) != 0);
    }

    /// <summary>Установка северного или западного ребра клетки-владельца в хранении.</summary>
    internal void SetOwnedEdge(CellPos owner, bool north, Edge edge)
    {
        Require(InStorage(owner), owner);
        ChunkLevel level = GetOrCreateLevel(owner);
        int index = LocalIndex(owner);
        byte flag = north ? ChunkLevel.NorthOpen : ChunkLevel.WestOpen;
        (north ? level.NorthWalls : level.WestWalls)[index] = edge.Wall;
        level.Flags[index] = edge.Open ? (byte)(level.Flags[index] | flag) : (byte)(level.Flags[index] & ~flag);
        _changes.Add(new WorldChange(owner, north ? WorldChangeKind.NorthEdge : WorldChangeKind.WestEdge));
    }

    /// <summary>Очистка журнала изменений после рассылки.</summary>
    internal void ClearChanges() => _changes.Clear();

    /// <summary>Созданные слои этажей чанка по возрастанию z.</summary>
    internal IEnumerable<(int Z, ChunkLevel Level)> ChunkLevels(int chunkX, int chunkY)
    {
        Chunk chunk = _chunks[(chunkY * ChunksX) + chunkX];
        for (int z = MinLevel; z <= MaxLevel; z++)
        {
            if (chunk.Level(z) is ChunkLevel level)
            {
                yield return (z, level);
            }
        }
    }

    /// <summary>Слой этажа чанка для заполнения из снимка, создаётся при необходимости.</summary>
    internal ChunkLevel LoadChunkLevel(int chunkX, int chunkY, int z) => _chunks[(chunkY * ChunksX) + chunkX].GetOrCreateLevel(z);

    /// <summary>Очистка чанка: клиент забывает чанк, который вышел из зоны.</summary>
    internal void UnloadChunk(int chunkX, int chunkY) => _chunks[(chunkY * ChunksX) + chunkX] = new Chunk();

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
    private Chunk ChunkOf(CellPos cell) => _chunks[(cell.Y / ChunkSize * ChunksX) + (cell.X / ChunkSize)];

    /// <summary>Слой этажа клетки или null.</summary>
    private ChunkLevel? Level(CellPos cell) => ChunkOf(cell).Level(cell.Z);

    /// <summary>Слой этажа клетки, создаётся при необходимости.</summary>
    private ChunkLevel GetOrCreateLevel(CellPos cell) => ChunkOf(cell).GetOrCreateLevel(cell.Z);
}
