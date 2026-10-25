using System;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using Xunit;

namespace DevAInt.Sim.Tests.Creatures;

/// <summary>Тесты <see cref="Vision"/>: конус, дальность, радиус вокруг, стены и двери, доли на границах.</summary>
public class VisionTest
{
    /// <summary>Номер стены.</summary>
    private const ushort Wall = 1;

    /// <summary>Номер двери.</summary>
    private const ushort Door = 2;

    /// <summary>Обзор человека из core: конус 140 градусов на 25 м и 1.5 м вокруг.</summary>
    private static readonly VisionDef _human = new() { Angle = 140, Range = 25, Near = 1.5 };

    /// <summary>Глаза в центре поля 40 на 40, взгляд на юг.</summary>
    private static readonly WorldPos _eye = new(20.5, 20.5, 0);

    /// <summary>Впереди в дальности видно, сзади за радиусом вокруг - нет, сзади вплотную - видно, дальше дальности - нет.</summary>
    [Fact]
    public void ConeRangeAndNear()
    {
        VisibilityMap map = Compute(Field(40, 40), _eye);

        Assert.Equal(1, map.Coverage(Cell(20, 30)));
        Assert.Equal(0, map.Coverage(Cell(20, 15)));
        Assert.Equal(1, map.Coverage(Cell(20, 19)));
        Assert.Equal(0, map.Coverage(Cell(20, 47)));
        Assert.Equal(1, map.Coverage(Cell(20, 20)));
        Assert.Equal(0, map.Coverage(new CellPos(20, 30, 1)));
    }

    /// <summary>Стена загораживает клетки за собой, открытая дверь пропускает взгляд, закрытая - нет.</summary>
    [Theory]
    [InlineData(Wall, false, 0f)]
    [InlineData(Door, false, 0f)]
    [InlineData(Door, true, 1f)]
    public void WallsAndDoors(ushort wall, bool open, float expected)
    {
        World world = Field(40, 40);
        for (int x = 15; x <= 25; x++)
        {
            world.SetEdge(Cell(x, 23), Side.North, new Edge(x == 20 ? wall : Wall, x == 20 && open));
        }

        VisibilityMap map = Compute(world, _eye);

        Assert.Equal(expected, map.Coverage(Cell(20, 26)));
        Assert.Equal(1, map.Coverage(Cell(20, 22)));
    }

    /// <summary>Стена сзади вплотную загораживает и в радиусе вокруг: сквозь стены не видно даже рядом.</summary>
    [Fact]
    public void NearStillNeedsSight()
    {
        World world = Field(40, 40);
        world.SetEdge(Cell(20, 20), Side.North, new Edge(Wall, false));

        Assert.Equal(0, Compute(world, _eye).Coverage(Cell(20, 19)));
    }

    /// <summary>Клетка на краю конуса и за углом стены видна частично.</summary>
    [Fact]
    public void EdgesArePartial()
    {
        World world = Field(40, 40);
        world.SetEdge(Cell(21, 24), Side.West, new Edge(Wall, false));
        VisibilityMap map = Compute(world, _eye);

        float coneEdge = map.Coverage(Cell(25, 22));
        Assert.InRange(coneEdge, 0.01f, 0.99f);
        Assert.InRange(map.Coverage(Cell(21, 26)), 0.01f, 0.99f);
    }

    /// <summary>Конус 360 градусов видит во все стороны.</summary>
    [Fact]
    public void FullCircle()
    {
        VisibilityMap map = Compute(Field(40, 40), _eye, _human with { Angle = 360 });

        Assert.Equal(1, map.Coverage(Cell(20, 10)));
    }

    /// <summary>Повторный расчёт в ту же карту не выделяет память.</summary>
    [Fact]
    public void RecomputeDoesNotAllocate()
    {
        World world = Field(40, 40);
        VisibilityMap map = Compute(world, _eye);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Vision.Compute(world, _eye, (0, 1), _human, map);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>Видимость с глаз eye, взгляд на юг.</summary>
    private static VisibilityMap Compute(World world, WorldPos eye, VisionDef? vision = null)
    {
        VisibilityMap map = new();
        Vision.Compute(world, eye, (0, 1), vision ?? _human, map);
        return map;
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
}
