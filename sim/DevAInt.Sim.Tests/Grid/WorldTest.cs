using System;
using DevAInt.Sim.Grid;
using Xunit;

namespace DevAInt.Sim.Tests.Grid;

/// <summary>Тесты хранения <see cref="World"/>: полы, рёбра, чанки и этажи.</summary>
public class WorldTest
{
    /// <summary>Пол ставится и читается, вне карты пола нет.</summary>
    [Fact]
    public void FloorIsStored()
    {
        World world = new(40, 40);

        world.SetFloor(new CellPos(33, 5, 0), 7);

        Assert.Equal(7, world.Floor(new CellPos(33, 5, 0)));
        Assert.True(world.IsWalkable(new CellPos(33, 5, 0)));
        Assert.False(world.IsWalkable(new CellPos(32, 5, 0)));
        Assert.Equal(0, world.Floor(new CellPos(-1, 5, 0)));
        Assert.Equal(0, world.Floor(new CellPos(40, 5, 0)));
    }

    /// <summary>Южное ребро клетки - это северное ребро соседа, восточное - западное соседа.</summary>
    [Fact]
    public void NeighborsShareEdges()
    {
        World world = new(40, 40);
        CellPos cell = new(31, 31, 0);

        world.SetEdge(cell, Side.South, new Edge(3, false));
        world.SetEdge(cell, Side.East, new Edge(4, true));

        Assert.Equal(new Edge(3, false), world.GetEdge(new CellPos(31, 32, 0), Side.North));
        Assert.Equal(new Edge(4, true), world.GetEdge(new CellPos(32, 31, 0), Side.West));
        Assert.Equal(Edge.None, world.GetEdge(cell, Side.North));
    }

    /// <summary>Открытость двери хранится отдельно для северного и западного ребра.</summary>
    [Fact]
    public void OpenFlagsAreIndependent()
    {
        World world = new(8, 8);
        CellPos cell = new(2, 2, 0);

        world.SetEdge(cell, Side.North, new Edge(1, true));
        world.SetEdge(cell, Side.West, new Edge(1, false));
        world.SetEdge(cell, Side.North, new Edge(1, false));

        Assert.False(world.GetEdge(cell, Side.North).Open);
        Assert.False(world.GetEdge(cell, Side.West).Open);
    }

    /// <summary>Крайние клетки карты имеют свои восточные и южные рёбра.</summary>
    [Fact]
    public void BorderCellsHaveOuterEdges()
    {
        World world = new(32, 32);
        CellPos corner = new(31, 31, 0);

        world.SetEdge(corner, Side.East, new Edge(2, false));
        world.SetEdge(corner, Side.South, new Edge(2, false));

        Assert.Equal(2, world.GetEdge(corner, Side.East).Wall);
        Assert.Equal(2, world.GetEdge(corner, Side.South).Wall);
    }

    /// <summary>Слой этажа создаётся, только когда на этаже что-то появилось.</summary>
    [Fact]
    public void LevelsAreCreatedOnDemand()
    {
        World world = new(64, 64);
        Assert.Equal(0, world.LevelsCreated);

        world.SetFloor(new CellPos(1, 1, 0), 1);
        world.SetFloor(new CellPos(2, 2, 0), 1);
        world.SetFloor(new CellPos(40, 1, 8), 1);
        _ = world.Floor(new CellPos(40, 40, -2));

        Assert.Equal(2, world.LevelsCreated);
    }

    /// <summary>Этажи от -2 до 8 внутри карты, остальное снаружи.</summary>
    [Fact]
    public void ContainsChecksLevels()
    {
        World world = new(10, 10);

        Assert.True(world.Contains(new CellPos(0, 0, -2)));
        Assert.True(world.Contains(new CellPos(9, 9, 8)));
        Assert.False(world.Contains(new CellPos(0, 0, 9)));
        Assert.False(world.Contains(new CellPos(0, 0, -3)));
    }

    /// <summary>Запись вне карты и неверный размер - исключения.</summary>
    [Fact]
    public void OutsideWritesThrow()
    {
        World world = new(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetFloor(new CellPos(10, 0, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetEdge(new CellPos(0, -1, 0), Side.North, new Edge(1, false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new World(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new World(World.MaxSize + 1, 10));
    }

    /// <summary>Проход: нужно проходимое ребро и пол у соседа.</summary>
    [Fact]
    public void CanCrossNeedsEdgeAndFloor()
    {
        World world = new(4, 4);
        CellPos a = new(1, 1, 0);
        world.SetFloor(a, 1);
        world.SetFloor(a.Neighbor(Side.East), 1);

        Assert.True(world.CanCross(a, Side.East));
        Assert.False(world.CanCross(a, Side.West));

        world.SetEdge(a, Side.East, new Edge(5, false));
        Assert.False(world.CanCross(a, Side.East));

        world.SetEdge(a, Side.East, new Edge(5, true));
        Assert.True(world.CanCross(a, Side.East));
    }
}
