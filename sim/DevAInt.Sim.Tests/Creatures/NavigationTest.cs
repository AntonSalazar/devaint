using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using Xunit;

namespace DevAInt.Sim.Tests.Creatures;

/// <summary>Тесты <see cref="Pathfinder"/> и <see cref="Sight"/> на маленьком мире из клеток.</summary>
public class NavigationTest
{
    /// <summary>Номер стены в тестовом мире.</summary>
    private const ushort Wall = 1;

    /// <summary>Номер двери в тестовом мире.</summary>
    private const ushort Door = 2;

    /// <summary>В открытом поле путь прямой и диагональный.</summary>
    [Fact]
    public void OpenFieldPathIsDiagonal()
    {
        World world = Field(10, 10);
        List<CellPos> path = [];

        Assert.True(new Pathfinder().Find(world, Cell(1, 1), Cell(5, 5), path));

        Assert.Equal([Cell(2, 2), Cell(3, 3), Cell(4, 4), Cell(5, 5)], path);
    }

    /// <summary>Стена поперёк поля обходится через проём.</summary>
    [Fact]
    public void PathGoesAroundWall()
    {
        World world = Field(10, 10);
        for (int y = 0; y < 8; y++)
        {
            world.SetEdge(Cell(5, y), Side.West, new Edge(Wall, false));
        }

        List<CellPos> path = [];

        Assert.True(new Pathfinder().Find(world, Cell(2, 2), Cell(8, 2), path));
        Assert.Contains(path, cell => cell.Y >= 8);
        AssertSteps(world, Cell(2, 2), path);
    }

    /// <summary>Закрытая дверь не пускает: путь ведёт к ближайшей клетке; открытая пускает.</summary>
    [Fact]
    public void ClosedDoorBlocksOpenDoorPasses()
    {
        World world = Room(doorOpen: false);
        List<CellPos> path = [];

        Assert.False(new Pathfinder().Find(world, Cell(5, 8), Cell(5, 4), path));
        Assert.Equal(Cell(5, 7), path.Last());

        world.SetEdge(Cell(5, 6), Side.South, new Edge(Door, true));
        Assert.True(new Pathfinder().Find(world, Cell(5, 8), Cell(5, 4), path));
        Assert.Contains(Cell(5, 6), path);
    }

    /// <summary>Диагональный ход мимо угла стены не срезает его.</summary>
    [Fact]
    public void DiagonalDoesNotCutCorners()
    {
        World world = Field(4, 4);
        world.SetEdge(Cell(1, 1), Side.East, new Edge(Wall, false));
        List<CellPos> path = [];

        Assert.True(new Pathfinder().Find(world, Cell(1, 1), Cell(2, 2), path));

        Assert.Equal([Cell(1, 2), Cell(2, 2)], path);

        // Угол закрывает стена у соседней клетки, а не у исходной
        World other = Field(4, 4);
        other.SetEdge(Cell(1, 2), Side.East, new Edge(Wall, false));
        Assert.True(new Pathfinder().Find(other, Cell(1, 1), Cell(2, 2), path));
        Assert.Equal([Cell(2, 1), Cell(2, 2)], path);
    }

    /// <summary>Недостижимая далёкая цель не съедает больше MaxNodes клеток, путь идёт к ближайшей.</summary>
    [Fact]
    public void SearchIsLimited()
    {
        World world = Field(200, 200);
        for (int y = 0; y < 200; y++)
        {
            world.SetEdge(Cell(150, y), Side.West, new Edge(Wall, false));
        }

        List<CellPos> path = [];

        Assert.False(new Pathfinder().Find(world, Cell(1, 100), Cell(190, 100), path));
        Assert.NotEmpty(path);
    }

    /// <summary>Видимость: открытое поле видно, стена и закрытая дверь загораживают, открытая дверь нет.</summary>
    [Fact]
    public void SightIsBlockedByWallsAndClosedDoors()
    {
        World world = Room(doorOpen: false);
        WorldPos inside = WorldPos.CenterOf(Cell(5, 4));
        WorldPos outside = WorldPos.CenterOf(Cell(5, 8));

        Assert.True(Sight.Clear(world, WorldPos.CenterOf(Cell(1, 8)), outside));
        Assert.False(Sight.Clear(world, inside, outside));
        Assert.False(Sight.Clear(world, WorldPos.CenterOf(Cell(4, 4)), WorldPos.CenterOf(Cell(8, 9))));

        world.SetEdge(Cell(5, 6), Side.South, new Edge(Door, true));
        Assert.True(Sight.Clear(world, inside, outside));
        Assert.False(Sight.Clear(world, inside, outside with { Z = 1 }));
    }

    /// <summary>Каждый шаг пути - сосед, через которого можно пройти.</summary>
    private static void AssertSteps(World world, CellPos from, List<CellPos> path)
    {
        CellPos previous = from;
        foreach (CellPos cell in path)
        {
            Assert.True(System.Math.Abs(cell.X - previous.X) <= 1 && System.Math.Abs(cell.Y - previous.Y) <= 1, $"{previous} -> {cell}");
            if (cell.X == previous.X || cell.Y == previous.Y)
            {
                Side side = cell.X > previous.X ? Side.East : cell.X < previous.X ? Side.West : cell.Y > previous.Y ? Side.South : Side.North;
                Assert.True(world.CanCross(previous, side), $"{previous} -> {cell} crosses a wall");
            }

            previous = cell;
        }
    }

    /// <summary>Клетка нулевого этажа.</summary>
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

    /// <summary>Поле 10 на 10 с комнатой 3..7 на 2..6 и дверью в южной стене у клетки 5,6.</summary>
    private static World Room(bool doorOpen)
    {
        World world = Field(10, 10);
        for (int x = 3; x <= 7; x++)
        {
            world.SetEdge(Cell(x, 2), Side.North, new Edge(Wall, false));
            world.SetEdge(Cell(x, 6), Side.South, new Edge(Wall, false));
        }

        for (int y = 2; y <= 6; y++)
        {
            world.SetEdge(Cell(3, y), Side.West, new Edge(Wall, false));
            world.SetEdge(Cell(7, y), Side.East, new Edge(Wall, false));
        }

        world.SetEdge(Cell(5, 6), Side.South, new Edge(Door, doorOpen));
        return world;
    }
}
