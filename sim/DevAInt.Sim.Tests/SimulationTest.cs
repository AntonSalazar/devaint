using System;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>Тесты <see cref="Simulation"/> с картой: движение сущностей в тике.</summary>
public class SimulationTest
{
    /// <summary>Сущность с вводом движется каждый тик и остаётся в комнате.</summary>
    [Fact]
    public void EntityMovesEachTick()
    {
        Simulation sim = Start();
        long id = sim.CreateEntity(new WorldPos(5.5, 6.5, 0));
        sim.Entity(id).Input = new MoveInput(1, 0);

        for (int i = 0; i < 10; i++)
        {
            sim.Tick();
        }

        Assert.Equal(7.0, sim.Entity(id).Position.X, 6);

        for (int i = 0; i < 50; i++)
        {
            sim.Tick();
        }

        Assert.Equal(10 - MoveParams.Default.Radius, sim.Entity(id).Position.X, 6);
    }

    /// <summary>Ускорение времени ускоряет и движение, и статы: за тик вдесятеро больше.</summary>
    [Fact]
    public void TimeScaleSpeedsUpMovementAndStats()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);
        Simulation normal = new(result, new ListLog(), "core:starter_room");
        Simulation fast = new(result, new ListLog(), "core:starter_room", new GameClock(timeScale: 10));
        long a = normal.CreateEntity(new WorldPos(4.5, 6.5, 0));
        long b = fast.CreateEntity(new WorldPos(4.5, 6.5, 0));
        normal.Entity(a).Input = new MoveInput(1, 0);
        fast.Entity(b).Input = new MoveInput(1, 0);
        int hunger = normal.StatTable.StatIndex("core:hunger");

        normal.Tick();
        fast.Tick();

        Assert.Equal(0.15, normal.Entity(a).Position.X - 4.5, 6);
        Assert.Equal(1.5, fast.Entity(b).Position.X - 4.5, 6);
        Assert.Equal(normal.Entity(a).Stats.Value(hunger) * 10, fast.Entity(b).Stats.Value(hunger), 9);
    }

    /// <summary>Сущность нельзя создать на клетке без пола.</summary>
    [Fact]
    public void EntityNeedsFloor()
    {
        Simulation sim = Start();

        Assert.Throws<ArgumentException>(() => sim.CreateEntity(new WorldPos(0.5, 0.5, 0)));
    }

    /// <summary>Sim с core и стартовой комнатой.</summary>
    private static Simulation Start()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return new Simulation(result, new ListLog(), "core:starter_room");
    }
}
