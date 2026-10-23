using System.Collections.Generic;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Defs;

/// <summary>Карта из мода: размер в клетках и операции построения по порядку.</summary>
public sealed record MapDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Ширина в клетках.</summary>
    public required int Width { get; init; }

    /// <summary>Высота в клетках.</summary>
    public required int Height { get; init; }

    /// <summary>Операции построения, применяются по порядку.</summary>
    public IReadOnlyList<MapOpDef> Build { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Width < 1 || Height < 1 || Width > World.MaxSize || Height > World.MaxSize)
        {
            yield return $"size {Width}x{Height} must be within 1..{World.MaxSize}";
            yield break;
        }

        for (int i = 0; i < Build.Count; i++)
        {
            foreach (string problem in Build[i].Validate(this, defs))
            {
                yield return $"build[{i}]: {problem}";
            }
        }
    }
}

/// <summary>Операция построения: ровно одно из floor, walls, door и прямоугольник или клетка.</summary>
public sealed record MapOpDef
{
    /// <summary>Пол на все клетки прямоугольника.</summary>
    public string? Floor { get; init; }

    /// <summary>Стены по контуру прямоугольника.</summary>
    public string? Walls { get; init; }

    /// <summary>Дверь на ребре side клетки x, y.</summary>
    public string? Door { get; init; }

    /// <summary>Западная клетка прямоугольника.</summary>
    public required int X { get; init; }

    /// <summary>Северная клетка прямоугольника.</summary>
    public required int Y { get; init; }

    /// <summary>Этаж.</summary>
    public int Z { get; init; }

    /// <summary>Ширина прямоугольника.</summary>
    public int W { get; init; } = 1;

    /// <summary>Высота прямоугольника.</summary>
    public int H { get; init; } = 1;

    /// <summary>Сторона клетки для двери.</summary>
    public Side? Side { get; init; }

    /// <summary>Открыта ли дверь при постройке.</summary>
    public bool Open { get; init; }

    /// <summary>Проблемы операции на этой карте.</summary>
    internal IEnumerable<string> Validate(MapDef map, DefDatabase defs)
    {
        int kinds = (Floor is null ? 0 : 1) + (Walls is null ? 0 : 1) + (Door is null ? 0 : 1);
        if (kinds != 1)
        {
            yield return "needs exactly one of floor, walls, door";
            yield break;
        }

        if (W < 1 || H < 1 || X < 0 || Y < 0 || X + W > map.Width || Y + H > map.Height)
        {
            yield return $"rect {X},{Y} {W}x{H} is outside the map {map.Width}x{map.Height}";
        }

        if (Z is < World.MinLevel or > World.MaxLevel)
        {
            yield return $"z {Z} must be within {World.MinLevel}..{World.MaxLevel}";
        }

        if (Floor is not null && !defs.TryGet<FloorDef>(Floor, out _))
        {
            yield return $"unknown floor {Floor}";
        }

        if (Walls is not null && (!defs.TryGet<WallDef>(Walls, out WallDef? wall) || wall.Kind != WallKind.Wall))
        {
            yield return $"unknown wall {Walls}";
        }

        if (Door is not null && (!defs.TryGet<WallDef>(Door, out WallDef? door) || door.Kind != WallKind.Door))
        {
            yield return $"unknown door {Door}";
        }

        if ((Door is null) != (Side is null))
        {
            yield return "side is required for door and only for door";
        }

        if (Open && Door is null)
        {
            yield return "open is only for door";
        }
    }
}
