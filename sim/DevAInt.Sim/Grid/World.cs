using System;
using System.Collections.Generic;
using DevAInt.Sim.Items;

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

    /// <summary>Объект поставлен.</summary>
    ObjectPlaced,

    /// <summary>Объект убран.</summary>
    ObjectRemoved,
}

/// <summary>Изменение мира: клетка-владелец, что именно поменялось и объект для изменений объектов.</summary>
internal readonly record struct WorldChange(CellPos Owner, WorldChangeKind Kind, WorldObject? Object = null);

/// <summary>Сетка мира из чанков: полы в клетках, стены и двери на рёбрах, объекты на клетках.</summary>
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

    /// <summary>Сторона региона сохранения в чанках.</summary>
    public const int RegionSize = 8;

    /// <summary>Наибольшая сторона карты в клетках.</summary>
    public const int MaxSize = 4096;

    /// <summary>Чанки построчно; хранение на клетку шире карты, чтобы у крайних клеток были свои восточные и южные рёбра.</summary>
    private readonly Chunk[] _chunks;

    /// <summary>Изменения полов и рёбер с последней очистки, для рассылки клиентам.</summary>
    private readonly List<WorldChange> _changes = [];

    /// <summary>Регионы, где менялись полы или рёбра с последнего сохранения.</summary>
    private readonly HashSet<(int X, int Y)> _dirtyRegions = [];

    /// <summary>Объекты по id.</summary>
    private readonly Dictionary<long, WorldObject> _objects = [];

    /// <summary>Содержимое контейнеров по id объекта.</summary>
    private readonly Dictionary<long, Inventory> _containers = [];

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

    /// <summary>Все объекты мира.</summary>
    public IReadOnlyCollection<WorldObject> Objects => _objects.Values;

    /// <summary>Число чанков по x в хранении, с запасом под внешние рёбра.</summary>
    internal int ChunksX { get; }

    /// <summary>Число чанков по y в хранении.</summary>
    internal int ChunksY { get; }

    /// <summary>Id следующего объекта.</summary>
    internal long NextObjectId { get; set; } = 1;

    /// <summary>Изменения полов и рёбер с последней очистки.</summary>
    internal IReadOnlyList<WorldChange> Changes => _changes;

    /// <summary>Регионы, где менялись полы или рёбра с последнего сохранения.</summary>
    internal IReadOnlyCollection<(int X, int Y)> DirtyRegions => _dirtyRegions;

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
        _dirtyRegions.Add(RegionOf(cell));
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

    /// <summary>Можно ли перейти из клетки в соседнюю: сосед в карте, ребро проходимо, у соседа есть пол и его не занимает объект, который мешает ходить.</summary>
    public bool CanCross(CellPos cell, Side side) => GetEdge(cell, side).IsPassable && IsFree(cell.Neighbor(side));

    /// <summary>Можно ли стоять на клетке телом: есть пол и объект не мешает ходить.</summary>
    public bool IsFree(CellPos cell) => IsWalkable(cell) && (ObjectFlags(cell) & ChunkLevel.MoveBlocked) == 0;

    /// <summary>Закрывает ли объект на клетке обзор.</summary>
    public bool BlocksSight(CellPos cell) => (ObjectFlags(cell) & ChunkLevel.SightBlocked) != 0;

    /// <summary>Объекты, чья северо-западная клетка лежит в чанке.</summary>
    public IReadOnlyList<WorldObject> ChunkObjects(int chunkX, int chunkY) => _chunks[(chunkY * ChunksX) + chunkX].Objects;

    /// <summary>Объект по id или null.</summary>
    public WorldObject? FindObject(long id) => _objects.GetValueOrDefault(id);

    /// <summary>Содержимое контейнера по id объекта, null - объекта нет или он не контейнер.</summary>
    public Inventory? Container(long objectId) => _containers.GetValueOrDefault(objectId);

    /// <summary>Объект, который занимает клетку, или null.</summary>
    public WorldObject? ObjectAt(CellPos cell)
    {
        if ((ObjectFlags(cell) & ChunkLevel.Occupied) == 0)
        {
            return null;
        }

        // Объект лежит в чанке своей северо-западной клетки, а она не дальше MaxSide клеток к западу и северу
        int chunkX = cell.X / ChunkSize;
        int chunkY = cell.Y / ChunkSize;
        for (int y = Math.Max(0, chunkY - 1); y <= chunkY; y++)
        {
            for (int x = Math.Max(0, chunkX - 1); x <= chunkX; x++)
            {
                foreach (WorldObject obj in _chunks[(y * ChunksX) + x].Objects)
                {
                    if (obj.Covers(cell))
                    {
                        return obj;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Почему объект нельзя поставить, или null, если можно: все клетки в карте, на полу, свободны от объектов и не разделены стеной.</summary>
    public string? PlaceProblem(WorldObject obj)
    {
        if (obj.Rotation is < 0 or >= WorldObject.Rotations)
        {
            return $"rotation {obj.Rotation} must be within 0..{WorldObject.Rotations - 1}";
        }

        for (int y = obj.Cell.Y; y < obj.Cell.Y + obj.Depth; y++)
        {
            for (int x = obj.Cell.X; x < obj.Cell.X + obj.Width; x++)
            {
                CellPos cell = new(x, y, obj.Cell.Z);
                if (!IsWalkable(cell))
                {
                    return $"cell {x},{y},{cell.Z} has no floor";
                }

                if ((ObjectFlags(cell) & ChunkLevel.Occupied) != 0)
                {
                    return $"cell {x},{y},{cell.Z} is taken by another object";
                }

                // Стена или дверь внутри прямоугольника разрезала бы объект
                bool westInside = x > obj.Cell.X && GetEdge(cell, Side.West).Wall != 0;
                bool northInside = y > obj.Cell.Y && GetEdge(cell, Side.North).Wall != 0;
                if (westInside || northInside)
                {
                    return $"a wall crosses the object at {x},{y},{cell.Z}";
                }
            }
        }

        return null;
    }

    /// <summary>Постановка объекта, для которого PlaceProblem вернул null; иначе исключение.</summary>
    public void PlaceObject(WorldObject obj)
    {
        if (PlaceProblem(obj) is string problem)
        {
            throw new ArgumentException($"object {obj.Id} cannot be placed: {problem}", nameof(obj));
        }

        AddObject(obj);
    }

    /// <summary>Удаление объекта по id, false если его нет.</summary>
    public bool RemoveObject(long id)
    {
        if (!_objects.Remove(id, out WorldObject? obj))
        {
            return false;
        }

        // Предметы контейнера пропадают вместе с ним: выкладывать их на пол пока некуда
        ChunkOf(obj.Cell).Objects.Remove(obj);
        _containers.Remove(id);
        MarkCells(obj, set: false);
        _changes.Add(new WorldChange(obj.Cell, WorldChangeKind.ObjectRemoved, obj));
        _dirtyRegions.Add(RegionOf(obj.Cell));
        return true;
    }

    /// <summary>Регион сохранения, в котором лежит клетка.</summary>
    internal static (int X, int Y) RegionOf(CellPos cell) => (cell.X / ChunkSize / RegionSize, cell.Y / ChunkSize / RegionSize);

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
        _dirtyRegions.Add(RegionOf(owner));
    }

    /// <summary>Объект без проверок места: из сохранения или снимка чанка, где он уже стоял.</summary>
    internal void AddObject(WorldObject obj)
    {
        _objects.Add(obj.Id, obj);
        ChunkOf(obj.Cell).Objects.Add(obj);
        if (obj.IsContainer)
        {
            _containers.Add(obj.Id, new Inventory());
        }

        MarkCells(obj, set: true);
        NextObjectId = Math.Max(NextObjectId, obj.Id + 1);
        _changes.Add(new WorldChange(obj.Cell, WorldChangeKind.ObjectPlaced, obj));
        _dirtyRegions.Add(RegionOf(obj.Cell));
    }

    /// <summary>Очистка журнала изменений после рассылки.</summary>
    internal void ClearChanges() => _changes.Clear();

    /// <summary>Регион нужно переписать при следующем сохранении, например после чтения старого формата.</summary>
    internal void MarkRegionDirty((int X, int Y) region) => _dirtyRegions.Add(region);

    /// <summary>Очистка списка изменённых регионов после сохранения или загрузки.</summary>
    internal void ClearDirtyRegions() => _dirtyRegions.Clear();

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

    /// <summary>Очистка чанка: клиент забывает чанк, который вышел из зоны, вместе с его объектами.</summary>
    internal void UnloadChunk(int chunkX, int chunkY)
    {
        Chunk chunk = _chunks[(chunkY * ChunksX) + chunkX];
        foreach (WorldObject obj in chunk.Objects)
        {
            // Объект у края мог занять клетки соседнего чанка: их флаги снимаются тоже
            _objects.Remove(obj.Id);
            _containers.Remove(obj.Id);
            MarkCells(obj, set: false);
        }

        _chunks[(chunkY * ChunksX) + chunkX] = new Chunk();
    }

    /// <summary>Флаги клеток чанка от объектов соседних чанков, которые на него заходят: после загрузки чанка из снимка.</summary>
    internal void MarkNeighbourObjects(int chunkX, int chunkY)
    {
        // Заходить на чанк могут только объекты из чанков к западу и северу: объект растёт на восток и юг
        for (int y = Math.Max(0, chunkY - 1); y <= chunkY; y++)
        {
            for (int x = Math.Max(0, chunkX - 1); x <= chunkX; x++)
            {
                if ((x, y) == (chunkX, chunkY))
                {
                    continue;
                }

                foreach (WorldObject obj in _chunks[(y * ChunksX) + x].Objects)
                {
                    MarkCells(obj, set: true);
                }
            }
        }
    }

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

    /// <summary>Флаги объекта на клетке, 0 - объекта нет или клетка вне хранения.</summary>
    private byte ObjectFlags(CellPos cell)
    {
        if (!InStorage(cell) || Level(cell) is not ChunkLevel level || level.ObjectFlags is not byte[] flags)
        {
            return 0;
        }

        return flags[LocalIndex(cell)];
    }

    /// <summary>Установка или снятие флагов объекта на всех его клетках в хранении.</summary>
    private void MarkCells(WorldObject obj, bool set)
    {
        byte flags = (byte)(ChunkLevel.Occupied | (obj.BlocksMove ? ChunkLevel.MoveBlocked : 0) | (obj.BlocksSight ? ChunkLevel.SightBlocked : 0));
        for (int y = obj.Cell.Y; y < obj.Cell.Y + obj.Depth; y++)
        {
            for (int x = obj.Cell.X; x < obj.Cell.X + obj.Width; x++)
            {
                CellPos cell = new(x, y, obj.Cell.Z);
                if (!InStorage(cell))
                {
                    continue;
                }

                ChunkLevel level = GetOrCreateLevel(cell);
                level.ObjectFlags ??= new byte[ChunkSize * ChunkSize];
                level.ObjectFlags[LocalIndex(cell)] = set ? flags : (byte)0;
            }
        }
    }

    /// <summary>Чанк клетки из хранения.</summary>
    private Chunk ChunkOf(CellPos cell) => _chunks[(cell.Y / ChunkSize * ChunksX) + (cell.X / ChunkSize)];

    /// <summary>Слой этажа клетки или null.</summary>
    private ChunkLevel? Level(CellPos cell) => ChunkOf(cell).Level(cell.Z);

    /// <summary>Слой этажа клетки, создаётся при необходимости.</summary>
    private ChunkLevel GetOrCreateLevel(CellPos cell) => ChunkOf(cell).GetOrCreateLevel(cell.Z);
}
