namespace DevAInt.Sim.Grid;

/// <summary>Чанк 32 на 32 клетки на все этажи; слой этажа создаётся, когда на нём что-то появилось.</summary>
internal sealed class Chunk
{
    /// <summary>Слои этажей по номеру от нижнего, null - на этаже пусто.</summary>
    private readonly ChunkLevel?[] _levels = new ChunkLevel?[World.LevelCount];

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

    /// <summary>Номера полов, 0 - пола нет.</summary>
    public ushort[] Floors { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Номера стен на северных рёбрах.</summary>
    public ushort[] NorthWalls { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Номера стен на западных рёбрах.</summary>
    public ushort[] WestWalls { get; } = new ushort[World.ChunkSize * World.ChunkSize];

    /// <summary>Флаги рёбер NorthOpen и WestOpen.</summary>
    public byte[] Flags { get; } = new byte[World.ChunkSize * World.ChunkSize];
}
