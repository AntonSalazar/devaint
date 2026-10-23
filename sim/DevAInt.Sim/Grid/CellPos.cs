namespace DevAInt.Sim.Grid;

/// <summary>Сторона клетки: x растёт на восток, y на юг.</summary>
public enum Side
{
    /// <summary>Сторона к меньшему y.</summary>
    North,

    /// <summary>Сторона к большему x.</summary>
    East,

    /// <summary>Сторона к большему y.</summary>
    South,

    /// <summary>Сторона к меньшему x.</summary>
    West,
}

/// <summary>Клетка мира: x на восток, y на юг, z - этаж, 0 - земля.</summary>
public readonly record struct CellPos(int X, int Y, int Z)
{
    /// <summary>Соседняя клетка того же этажа со стороны side.</summary>
    public CellPos Neighbor(Side side)
    {
        return side switch
        {
            Side.North => this with { Y = Y - 1 },
            Side.East => this with { X = X + 1 },
            Side.South => this with { Y = Y + 1 },
            _ => this with { X = X - 1 },
        };
    }
}

/// <summary>Ребро клетки: номер стены, 0 - пусто, и флаг открытой двери.</summary>
public readonly record struct Edge(ushort Wall, bool Open)
{
    /// <summary>Пустое ребро.</summary>
    public static readonly Edge None = default;

    /// <summary>Можно ли пройти: ребро пустое или на нём открытая дверь.</summary>
    public bool IsPassable => Wall == 0 || Open;
}
