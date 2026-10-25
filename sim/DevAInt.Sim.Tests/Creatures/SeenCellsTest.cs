using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using Xunit;

namespace DevAInt.Sim.Tests.Creatures;

/// <summary>Тесты <see cref="SeenCells"/>: увиденное запоминается и не забывается, когда отвернулся.</summary>
public class SeenCellsTest
{
    /// <summary>Клетки впереди запоминаются, сзади нет; после поворота прежние остаются увиденными.</summary>
    [Fact]
    public void RemembersWhatWasSeen()
    {
        World world = new(40, 40);
        VisionDef vision = new() { Angle = 90, Range = 10 };
        WorldPos eye = new(20.5, 20.5, 0);
        VisibilityMap map = new();
        SeenCells seen = new();

        Vision.Compute(world, eye, (0, 1), vision, map);
        seen.Mark(world, eye, (0, 1), vision, map);
        bool behindBefore = seen.IsSeen(new CellPos(20, 15, 0));
        Vision.Compute(world, eye, (0, -1), vision, map);
        seen.Mark(world, eye, (0, -1), vision, map);

        Assert.True(seen.IsSeen(new CellPos(20, 25, 0)), "seen ahead");
        Assert.False(behindBefore, "not seen behind at first");
        Assert.True(seen.IsSeen(new CellPos(20, 15, 0)), "seen after turning");
        Assert.True(seen.IsSeen(new CellPos(20, 25, 0)), "still remembered after turning away");
        Assert.False(seen.IsSeen(new CellPos(35, 20, 0)), "never seen");
        Assert.False(seen.IsSeen(new CellPos(20, 25, 1)), "other floor");
        Assert.False(seen.IsSeen(new CellPos(-1, 0, 0)), "outside the map");
        Assert.Equal(SeenCells.AllPoints, seen.Points(new CellPos(20, 25, 0)));
    }

    /// <summary>Клетка на краю конуса запоминается по точкам: только та её часть, что попала в конус.</summary>
    [Fact]
    public void EdgeCellRemembersOnlyVisiblePoints()
    {
        World world = new(40, 40);
        VisionDef vision = new() { Angle = 90, Range = 10 };
        WorldPos eye = new(20.5, 20.5, 0);
        VisibilityMap map = new();
        SeenCells seen = new();

        Vision.Compute(world, eye, (0, 1), vision, map);
        seen.Mark(world, eye, (0, 1), vision, map);

        // Край конуса идёт по диагонали клетки 25, 25: южный запад клетки в конусе, северный восток нет
        ushort points = seen.Points(new CellPos(25, 25, 0));
        Assert.True((points & Bit(0, 3)) != 0, "south west point is in the cone");
        Assert.True((points & Bit(3, 0)) == 0, "north east point is outside the cone");
    }

    /// <summary>Клетка за углом стены запоминается частью: точки в тени стены не отмечаются.</summary>
    [Fact]
    public void WallShadowSplitsCell()
    {
        World world = new(40, 40);
        world.SetEdge(new CellPos(21, 22, 0), Side.North, new Edge(1, false));
        VisionDef vision = new() { Angle = 360, Range = 10 };
        WorldPos eye = new(20.5, 20.5, 0);
        VisibilityMap map = new();
        SeenCells seen = new();

        Vision.Compute(world, eye, (0, 1), vision, map);
        seen.Mark(world, eye, (0, 1), vision, map);

        // Стена на северном ребре клетки 21, 22 бросает тень, край которой идёт по диагонали клетки 23, 23
        ushort points = seen.Points(new CellPos(23, 23, 0));
        Assert.NotEqual(0, points);
        Assert.NotEqual(SeenCells.AllPoints, points);
        for (int y = 0; y < SeenCells.PointsPerSide; y++)
        {
            for (int x = 0; x < SeenCells.PointsPerSide; x++)
            {
                WorldPos point = new(23 + ((x + 0.5) / 4), 23 + ((y + 0.5) / 4), 0);
                Assert.Equal(Sight.Clear(world, eye, point), (points & Bit(x, y)) != 0);
            }
        }
    }

    /// <summary>Бит точки x, y клетки.</summary>
    private static int Bit(int x, int y) => 1 << ((y * SeenCells.PointsPerSide) + x);
}
