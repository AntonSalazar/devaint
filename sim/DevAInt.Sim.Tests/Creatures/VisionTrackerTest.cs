using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using Xunit;

namespace DevAInt.Sim.Tests.Creatures;

/// <summary>Тесты <see cref="VisionTracker"/>: пересчёт только по делу.</summary>
public class VisionTrackerTest
{
    /// <summary>Обзор для тестов.</summary>
    private static readonly VisionDef _vision = new() { Angle = 90, Range = 10 };

    /// <summary>Первый раз считается; мелкий шаг и мелкий поворот - нет; шаг от порога, поворот и смена стен - да.</summary>
    [Fact]
    public void RecomputesOnlyOnChange()
    {
        World world = new(40, 40);
        VisionTracker tracker = new();

        bool first = tracker.Update(world, new WorldPos(20.5, 20.5, 0), (0, 1), _vision);
        bool small = tracker.Update(world, new WorldPos(20.6, 20.6, 0), (0.02, 0.9998), _vision);
        bool moved = tracker.Update(world, new WorldPos(20.8, 20.6, 0), (0.02, 0.9998), _vision);
        bool sameCellAgain = tracker.Update(world, new WorldPos(20.9, 20.6, 0), (0.02, 0.9998), _vision);
        bool newCell = tracker.Update(world, new WorldPos(21.2, 20.6, 0), (0.02, 0.9998), _vision);
        bool turned = tracker.Update(world, new WorldPos(21.2, 20.6, 0), (1, 0), _vision);
        tracker.Invalidate();
        bool walls = tracker.Update(world, new WorldPos(21.2, 20.6, 0), (1, 0), _vision);

        Assert.Equal([true, false, true, false, true, true, true], [first, small, moved, sameCellAgain, newCell, turned, walls]);
        Assert.True(tracker.Seen.IsSeen(new CellPos(21, 25, 0)), "seen while facing south");
        Assert.Equal(1, tracker.Map.Coverage(new CellPos(25, 20, 0)));
    }
}
