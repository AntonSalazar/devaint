using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Grid;

/// <summary>Итог этапа 4: комната core:starter_room из настоящего мода core.</summary>
public class StarterRoomTest
{
    /// <summary>Таблица полов и стен core.</summary>
    private static readonly WorldTable _table;

    /// <summary>Мир, построенный по карте.</summary>
    private static readonly World _world;

    /// <summary>Загрузка core и постройка карты один раз на все тесты.</summary>
    static StarterRoomTest()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        _table = new WorldTable(result.Defs);
        _world = MapBuilder.Build(result.Defs.Get<MapDef>("core:starter_room"), _table);
    }

    /// <summary>Внутри комнаты можно ходить между клетками, сквозь стены нельзя.</summary>
    [Fact]
    public void WallsBlockAndInsideIsOpen()
    {
        CellPos inside = new(5, 5, 0);

        Assert.True(_world.CanCross(inside, Side.East));
        Assert.True(_world.CanCross(inside, Side.South));
        Assert.False(_world.CanCross(new CellPos(4, 5, 0), Side.West));
        Assert.False(_world.CanCross(new CellPos(3, 5, 0), Side.East));
        Assert.False(_world.CanCross(new CellPos(5, 4, 0), Side.North));
    }

    /// <summary>Дверь в южной стене закрыта и стоит на нужном ребре.</summary>
    [Fact]
    public void DoorIsClosedOnSouthWall()
    {
        CellPos doorCell = new(6, 8, 0);
        Edge door = _world.GetEdge(doorCell, Side.South);

        Assert.Equal(WallKind.Door, _table.Wall(door.Wall).Kind);
        Assert.False(door.Open);
        Assert.False(_world.CanCross(doorCell, Side.South));
        Assert.Equal(door, _world.GetEdge(doorCell.Neighbor(Side.South), Side.North));
    }

    /// <summary>Открытая дверь пропускает в обе стороны.</summary>
    [Fact]
    public void OpenDoorLetsThrough()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);
        World world = MapBuilder.Build(result.Defs.Get<MapDef>("core:starter_room"), _table);
        CellPos doorCell = new(6, 8, 0);
        Edge door = world.GetEdge(doorCell, Side.South);

        world.SetEdge(doorCell, Side.South, door with { Open = true });

        Assert.True(world.CanCross(doorCell, Side.South));
        Assert.True(world.CanCross(doorCell.Neighbor(Side.South), Side.North));
    }

    /// <summary>ASCII-дамп показывает двор, комнату и закрытую дверь.</summary>
    [Fact]
    public void AsciiShowsRoom()
    {
        string expected = """
            . . . . . . . . . .

            . . . . . . . . . .
               +-----------+
            . .|. . . . . .|. .
               |           |
            . .|. . . . . .|. .
               |           |
            . .|. . . . . .|. .
               |           |
            . .|. . . . . .|. .
               |           |
            . .|. . . . . .|. .
               +----D------+
            . . . . . . . . . .

            . . . . . . . . . .

            . . . . . . . . . .

            """;

        Assert.Equal(expected.Replace("\r\n", "\n"), WorldDump.Ascii(_world, _table));
    }

    /// <summary>Пустой этаж даёт пустой дамп.</summary>
    [Fact]
    public void EmptyLevelIsEmpty() => Assert.Equal(string.Empty, WorldDump.Ascii(_world, _table, z: 1));
}
