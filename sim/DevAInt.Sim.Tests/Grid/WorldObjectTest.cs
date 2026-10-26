using System.Collections.Generic;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Grid;

/// <summary>Тесты объектов мира: постановка, поворот, занятые клетки, граница чанка, движение, пути и видимость.</summary>
public class WorldObjectTest
{
    /// <summary>Ящик 1 на 1, мешает ходить.</summary>
    private static readonly ObjectDef _crate = new() { Id = "test:crate" };

    /// <summary>Стол 2 на 1, мешает ходить, но не закрывает обзор.</summary>
    private static readonly ObjectDef _table = new() { Id = "test:table", W = 2 };

    /// <summary>Шкаф 1 на 1, закрывает обзор.</summary>
    private static readonly ObjectDef _wardrobe = new() { Id = "test:wardrobe", BlocksSight = true };

    /// <summary>Ковёр 3 на 2: занимает клетки, но не мешает ходить.</summary>
    private static readonly ObjectDef _rug = new() { Id = "test:rug", W = 3, H = 2, BlocksMove = false };

    /// <summary>Поворот на четверть оборота меняет ширину и глубину местами, на пол-оборота - нет.</summary>
    [Fact]
    public void RotationSwapsSize()
    {
        WorldObject turned = WorldObject.Create(1, 1, _table, Cell(0, 0), 1);
        WorldObject upside = WorldObject.Create(2, 1, _table, Cell(0, 0), 2);

        Assert.Equal((1, 2), (turned.Width, turned.Depth));
        Assert.Equal((2, 1), (upside.Width, upside.Depth));
    }

    /// <summary>Объекту нужен пол под всеми клетками, свободные клетки, верный поворот и ни одной стены внутри.</summary>
    [Fact]
    public void PlaceProblems()
    {
        World world = Field(10, 10);
        world.SetEdge(Cell(6, 2), Side.West, new Edge(1, false));
        world.PlaceObject(Object(1, _crate, 2, 2));

        Assert.Null(world.PlaceProblem(Object(2, _table, 3, 2)));
        Assert.Equal("cell 2,2,0 is taken by another object", world.PlaceProblem(Object(2, _table, 1, 2)));
        Assert.Equal("cell 10,5,0 has no floor", world.PlaceProblem(Object(2, _table, 9, 5)));
        Assert.Equal("a wall crosses the object at 6,2,0", world.PlaceProblem(Object(2, _table, 5, 2)));
        Assert.Equal("rotation 4 must be within 0..3", world.PlaceProblem(Object(2, _table, 3, 2, rotation: 4)));
        Assert.Throws<System.ArgumentException>(() => world.PlaceObject(Object(2, _crate, 2, 2)));
    }

    /// <summary>Клетки объекта заняты по его правилам, поиск по клетке находит объект, удаление освобождает клетки.</summary>
    [Fact]
    public void FlagsFollowObject()
    {
        World world = Field(10, 10);
        world.PlaceObject(Object(1, _table, 2, 2, rotation: 1));
        world.PlaceObject(Object(2, _rug, 5, 5));
        world.PlaceObject(Object(3, _wardrobe, 8, 8));

        Assert.False(world.IsFree(Cell(2, 3)), "rotated table takes 2,3");
        Assert.True(world.IsFree(Cell(3, 2)), "rotated table leaves 3,2");
        Assert.True(world.IsFree(Cell(7, 6)), "rug does not block walking");
        Assert.Equal(2, world.ObjectAt(Cell(7, 6))?.Id);
        Assert.True(world.BlocksSight(Cell(8, 8)), "wardrobe blocks sight");
        Assert.False(world.BlocksSight(Cell(2, 2)), "table does not block sight");
        Assert.Equal(4, world.NextObjectId);

        Assert.True(world.RemoveObject(1));
        Assert.False(world.RemoveObject(1));
        Assert.True(world.IsFree(Cell(2, 3)), "removed table frees its cells");
        Assert.Null(world.ObjectAt(Cell(2, 3)));
        Assert.Null(world.FindObject(1));
    }

    /// <summary>Объект у края чанка занимает клетки соседнего; выгрузка чанков снимает и возвращает их, как у клиента.</summary>
    [Fact]
    public void ObjectAcrossChunkBorder()
    {
        World world = Field(64, 64);
        world.PlaceObject(Object(1, _table, 31, 31));
        CellPos east = Cell(32, 31);

        bool taken = !world.IsFree(east);
        WorldObject? found = world.ObjectAt(east);

        // Выгрузка соседнего чанка забывает его клетки; снимок вернёт пол, а флаги объекта ставит MarkNeighbourObjects
        world.UnloadChunk(1, 0);
        WorldObject? afterUnload = world.ObjectAt(east);
        world.SetFloor(east, 1);
        world.MarkNeighbourObjects(1, 0);
        WorldObject? afterLoad = world.ObjectAt(east);
        bool blocked = !world.IsFree(east);
        world.UnloadChunk(0, 0);

        Assert.True(taken, "cell in the next chunk is taken");
        Assert.Equal(1, found?.Id);
        Assert.Null(afterUnload);
        Assert.Equal(1, afterLoad?.Id);
        Assert.True(blocked, "neighbour object blocks walking again");
        Assert.Null(world.ObjectAt(east));
        Assert.True(world.IsFree(east), "unloading the owner chunk frees the neighbour cell");
        Assert.Null(world.FindObject(1));
    }

    /// <summary>Тело упирается в ящик, как в стену, и обходит его по пути.</summary>
    [Fact]
    public void BodyAndPathAvoidObjects()
    {
        World world = Field(10, 10);
        world.PlaceObject(Object(1, _crate, 5, 3));
        WorldPos position = new(3.5, 3.5, 0);
        for (int i = 0; i < 30; i++)
        {
            position = Movement.Step(world, position, new MoveInput(1, 0), GameClock.RealSecondsPerTick, MoveParams.Default);
        }

        List<CellPos> path = [];
        bool found = new Pathfinder().Find(world, Cell(4, 3), Cell(6, 3), path);

        Assert.Equal(5 - MoveParams.Default.Radius, position.X, 6);
        Assert.True(found, "path exists");
        Assert.DoesNotContain(Cell(5, 3), path);
    }

    /// <summary>Шкаф закрывает то, что за ним, но сам виден; стол обзор не закрывает.</summary>
    [Fact]
    public void WardrobeBlocksSightTableDoesNot()
    {
        World world = Field(10, 10);
        world.PlaceObject(Object(1, _wardrobe, 5, 3));
        world.PlaceObject(Object(2, _table, 5, 6));
        WorldPos eye = new(2.5, 3.5, 0);

        Assert.False(Sight.Clear(world, eye, new WorldPos(8.5, 3.5, 0)), "behind the wardrobe");
        Assert.True(Sight.Clear(world, eye, new WorldPos(5.5, 3.5, 0)), "the wardrobe itself");
        Assert.True(Sight.Clear(world, new WorldPos(2.5, 6.5, 0), new WorldPos(8.5, 6.5, 0)), "over the table");
        Assert.Equal(2.5 / 6, Sight.Blocked(world, eye, new WorldPos(8.5, 3.5, 0))!.Value, 6);
    }

    /// <summary>Стартовая комната ставит свою мебель; объект без места - ошибка в лог, постройка идёт дальше.</summary>
    [Fact]
    public void MapPlacesObjects()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("test", depends: "core").Write("test/data/map.jsonc", """
            { "maps": [ { "id": "test:map", "width": 8, "height": 8, "spawn": { "points": [ { "x": 0, "y": 0 } ] },
              "build": [ { "floor": "core:wood_floor", "x": 0, "y": 0, "w": 8, "h": 8 },
                         { "object": "core:table", "x": 1, "y": 1, "rotation": 1 },
                         { "object": "core:crate", "x": 1, "y": 2 },
                         { "object": "core:crate", "x": 7, "y": 7 } ] } ] }
            """);
        ModLoadResult loaded = mods.Load("core", "test");
        Assert.True(loaded.Ok, string.Join("\n", loaded.Errors));
        ListLog log = new();

        World world = MapBuilder.Build(loaded.Defs.Get<MapDef>("test:map"), new WorldTable(loaded.Defs), log);
        World starter = MapBuilder.Build(loaded.Defs.Get<MapDef>("core:starter_room"), new WorldTable(loaded.Defs));

        Assert.Equal(2, world.Objects.Count);
        Assert.Equal("map test:map: build[2]: object core:crate: cell 1,2,0 is taken by another object", Assert.Single(log.Errors));
        Assert.Equal(4, starter.Objects.Count);
        Assert.True(starter.BlocksSight(Cell(9, 4)), "wardrobe in the north east corner");
    }

    /// <summary>Объект с id вида def в клетке x, y.</summary>
    private static WorldObject Object(long id, ObjectDef def, int x, int y, int rotation = 0) => WorldObject.Create(id, 1, def, Cell(x, y), rotation);

    /// <summary>Клетка первого этажа.</summary>
    private static CellPos Cell(int x, int y) => new(x, y, 0);

    /// <summary>Поле с полом на всех клетках.</summary>
    private static World Field(int width, int height)
    {
        World world = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                world.SetFloor(Cell(x, y), 1);
            }
        }

        return world;
    }
}
