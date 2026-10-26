using System.Collections.Generic;

namespace DevAInt.Sim.Grid;

/// <summary>Чанк 32 на 32 клетки на все этажи; слой этажа создаётся, когда на нём что-то появилось.</summary>
internal sealed class Chunk
{
    /// <summary>Слои этажей по номеру от нижнего, null - на этаже пусто.</summary>
    private readonly ChunkLevel?[] _levels = new ChunkLevel?[World.LevelCount];

    /// <summary>Объекты, чья северо-западная клетка лежит в этом чанке.</summary>
    public List<WorldObject> Objects { get; } = [];

    /// <summary>Число созданных слоев.</summary>
    public int LevelsCreated { get; private set; }

    /// <summary>Слой этажа или null, если на нём пусто.</summary>
    public ChunkLevel? Level(int z) => _levels[z - World.MinLevel];

    /// <summary>Слой этажа, создаётся при первом обращении.</summary>
    public ChunkLevel GetOrCreateLevel(int z)
    {
        int index = z - World.MinLevel;
        if (_levels[index] is ChunkLevel level)
        {
            return level;
        }

        LevelsCreated++;
        return _levels[index] = new ChunkLevel();
    }
}

/// <summary>Один этаж чанка: полы и северные и западные рёбра клеток.</summary>
internal sealed class ChunkLevel
{
    /// <summary>Флаг открытой двери на северном ребре.</summary>
    public const byte NorthOpen = 1;

    /// <summary>Флаг открытой двери на западном ребре.</summary>
    public const byte WestOpen = 2;

    /// <summary>Сдвиг вида северной стены, WallKind, во флагах рёбер: два бита.</summary>
    public const int NorthKindShift = 2;

    /// <summary>Сдвиг вида западной стены, WallKind, во флагах рёбер: два бита.</summary>
    public const int WestKindShift = 4;

    /// <summary>Флаг клетки: её занимает объект.</summary>
    public const byte Occupied = 1;

    /// <summary>Флаг клетки: объект мешает ходить.</summary>
    public const byte MoveBlocked = 2;

    /// <summary>Флаг клетки: объект закрывает обзор.</summary>
    public const byte SightBlocked = 4;

    /// <summary>Флаг клетки: верх лестницы или проём над ним, её рёбра проверяет Stairs.</summary>
    public const byte StairsEdge = 8;

    /// <summary>Номера полов, 0 - пола нет.</summary>
    public ushort[] Floors { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Номера стен на северных рёбрах.</summary>
    public ushort[] NorthWalls { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Номера стен на западных рёбрах.</summary>
    public ushort[] WestWalls { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Номера створок на северных рёбрах, 0 - нет.</summary>
    public ushort[] NorthFills { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Номера створок на западных рёбрах, 0 - нет.</summary>
    public ushort[] WestFills { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Флаги рёбер NorthOpen и WestOpen и виды стен по сдвигам NorthKindShift и WestKindShift.</summary>
    public byte[] Flags { get; } = new byte[World.ChunkSize * World.ChunkSize];

    /// <summary>Флаги клеток Occupied, MoveBlocked и SightBlocked; создаются при первом объекте на слое.</summary>
    public byte[]? ObjectFlags { get; set; }
}
