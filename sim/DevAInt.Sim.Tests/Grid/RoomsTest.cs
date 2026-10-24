using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Grid;

/// <summary>Тесты <see cref="Rooms"/> на стартовой комнате core.</summary>
public class RoomsTest
{
    /// <summary>Моды core.</summary>
    private static readonly ModLoadResult _core = ModLoader.Load(RepoMods.Root, ["core"]);

    /// <summary>Комната 6 на 5 клеток находится из любой своей клетки, дверь закрыта или открыта.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StarterRoomIsFound(bool doorOpen)
    {
        World world = Room(doorOpen);

        HashSet<CellPos>? room = Rooms.Find(world, new CellPos(6, 6, 0));

        Assert.NotNull(room);
        Assert.Equal(30, room.Count);
        Assert.Contains(new CellPos(4, 4, 0), room);
        Assert.Contains(new CellPos(9, 8, 0), room);
        Assert.Equal(room, Rooms.Find(world, new CellPos(9, 8, 0)));
    }

    /// <summary>Двор вокруг комнаты выходит к краю пола без стены: это улица, комнаты нет.</summary>
    [Fact]
    public void YardIsOutdoors() => Assert.Null(Rooms.Find(Room(doorOpen: true), new CellPos(3, 10, 0)));

    /// <summary>Клетка без пола - не комната; комната больше предела - тоже.</summary>
    [Fact]
    public void NoFloorOrTooBig()
    {
        World world = Room(doorOpen: false);

        Assert.Null(Rooms.Find(world, new CellPos(20, 20, 0)));
        Assert.Null(Rooms.Find(world, new CellPos(6, 6, 0), maxCells: 10));
    }

    /// <summary>Стартовая комната core.</summary>
    private static World Room(bool doorOpen)
    {
        Assert.True(_core.Ok, string.Join("\n", _core.Errors));
        World world = MapBuilder.Build(_core.Defs.Get<MapDef>("core:starter_room"), new WorldTable(_core.Defs));
        CellPos door = new(6, 8, 0);
        world.SetEdge(door, Side.South, world.GetEdge(door, Side.South) with { Open = doorOpen });
        return world;
    }
}
