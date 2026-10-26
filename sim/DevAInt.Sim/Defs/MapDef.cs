using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Появление игроков: точки, вид существа и стартовые предметы.</summary>
    public SpawnDef Spawn { get; init; } = new();

    /// <summary>Существа, которые появляются в новом мире.</summary>
    public IReadOnlyList<CreatureSpawnDef> Creatures { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Width < 1 || Height < 1 || Width > World.MaxSize || Height > World.MaxSize)
        {
            yield return $"size {Width}x{Height} must be within 1..{World.MaxSize}";
            yield break;
        }

        if (Spawn.Points.Count == 0)
        {
            yield return "spawn needs at least one point";
        }

        for (int i = 0; i < Spawn.Points.Count; i++)
        {
            SpawnPointDef point = Spawn.Points[i];
            if (point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height || point.Z < World.MinLevel || point.Z > World.MaxLevel)
            {
                yield return $"spawn.points[{i}]: cell {point.X},{point.Y},{point.Z} is outside the map";
            }
        }

        if (Spawn.Creature is not null && !defs.TryGet<CreatureDef>(Spawn.Creature, out _))
        {
            yield return $"spawn: unknown creature {Spawn.Creature}";
        }

        for (int i = 0; i < Creatures.Count; i++)
        {
            CreatureSpawnDef spawn = Creatures[i];
            if (!defs.TryGet<CreatureDef>(spawn.Creature, out _))
            {
                yield return $"creatures[{i}]: unknown creature {spawn.Creature}";
            }

            if (spawn.X < 0 || spawn.Y < 0 || spawn.X >= Width || spawn.Y >= Height || spawn.Z < World.MinLevel || spawn.Z > World.MaxLevel)
            {
                yield return $"creatures[{i}]: cell {spawn.X},{spawn.Y},{spawn.Z} is outside the map";
            }

            if (spawn.Count < 1)
            {
                yield return $"creatures[{i}]: count {spawn.Count} must be positive";
            }
        }

        foreach (string item in Spawn.Items)
        {
            if (!defs.TryGet<ItemDef>(item, out _))
            {
                yield return $"spawn: unknown item {item}";
            }
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

/// <summary>Появление игроков на карте: точки, из которых выбирает хук мода choose_spawn или ядро.</summary>
public sealed record SpawnDef
{
    /// <summary>Клетки появления по порядку.</summary>
    public IReadOnlyList<SpawnPointDef> Points { get; init; } = [];

    /// <summary>Предметы, с которыми появляется игрок.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>Вид существа игрока, null - сущность без вида со всеми статами.</summary>
    public string? Creature { get; init; }
}

/// <summary>Клетка появления игрока.</summary>
public sealed record SpawnPointDef
{
    /// <summary>Клетка по x.</summary>
    public required int X { get; init; }

    /// <summary>Клетка по y.</summary>
    public required int Y { get; init; }

    /// <summary>Этаж.</summary>
    public int Z { get; init; }
}

/// <summary>Существа одного вида в клетке карты.</summary>
public sealed record CreatureSpawnDef
{
    /// <summary>Вид существа.</summary>
    public required string Creature { get; init; }

    /// <summary>Клетка по x.</summary>
    public required int X { get; init; }

    /// <summary>Клетка по y.</summary>
    public required int Y { get; init; }

    /// <summary>Этаж.</summary>
    public int Z { get; init; }

    /// <summary>Сколько существ.</summary>
    public int Count { get; init; } = 1;
}

/// <summary>Операция построения: ровно одно из floor, walls, door, window, object и прямоугольник или клетка.</summary>
public sealed record MapOpDef
{
    /// <summary>Пол на все клетки прямоугольника.</summary>
    public string? Floor { get; init; }

    /// <summary>Стены по контуру прямоугольника.</summary>
    public string? Walls { get; init; }

    /// <summary>Дверь в своём дверном проёме на ребре side клетки x, y: проём ставится вместе с ней.</summary>
    public string? Door { get; init; }

    /// <summary>Окно в своей оконной раме на ребре side клетки x, y: рама ставится вместе с ним.</summary>
    public string? Window { get; init; }

    /// <summary>Объект северо-западной клеткой в x, y с поворотом rotation.</summary>
    public string? Object { get; init; }

    /// <summary>Поворот объекта: 0..3, по четверти оборота; нечётный меняет ширину и глубину местами.</summary>
    public int Rotation { get; init; }

    /// <summary>Предметы, которые лежат в контейнере объекта в новом мире.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

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

    /// <summary>Сторона клетки для двери или окна.</summary>
    public Side? Side { get; init; }

    /// <summary>Открыта ли дверь или окно при постройке.</summary>
    public bool Open { get; init; }

    /// <summary>Проблемы операции на этой карте.</summary>
    internal IEnumerable<string> Validate(MapDef map, DefDatabase defs)
    {
        int kinds = (Floor is null ? 0 : 1) + (Walls is null ? 0 : 1) + (Door is null ? 0 : 1) + (Window is null ? 0 : 1) + (Object is null ? 0 : 1);
        if (kinds != 1)
        {
            yield return "needs exactly one of floor, walls, door, window, object";
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

        if (Door is not null && !defs.TryGet<DoorDef>(Door, out _))
        {
            yield return $"unknown door {Door}";
        }

        if (Window is not null && !defs.TryGet<WindowDef>(Window, out _))
        {
            yield return $"unknown window {Window}";
        }

        bool fill = Door is not null || Window is not null;
        if (fill != (Side is not null))
        {
            yield return "side is required for door and window and only for them";
        }

        if (Open && !fill)
        {
            yield return "open is only for door and window";
        }

        ObjectDef? obj = null;
        if (Object is not null && defs.TryGet<ObjectDef>(Object, out ObjectDef? found))
        {
            obj = found;
        }
        else if (Object is not null)
        {
            yield return $"unknown object {Object}";
        }

        if (Items.Count > 0 && obj?.Container is null)
        {
            yield return Object is null ? "items are only for object" : $"object {Object} is not a container";
        }

        foreach (string item in Items.Where(item => !defs.TryGet<ItemDef>(item, out _)))
        {
            yield return $"unknown item {item}";
        }

        if (Object is null ? Rotation != 0 : Rotation is < 0 or >= WorldObject.Rotations)
        {
            yield return Object is null ? "rotation is only for object" : $"rotation {Rotation} must be within 0..{WorldObject.Rotations - 1}";
        }
    }
}
