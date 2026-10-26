using System.Collections.Generic;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using DevAInt.Sim.Tests.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Motion;

/// <summary>Тесты лестницы стартовой комнаты: по западной стене с клетки 4,7 вверх на север к выходу 4,4 второго этажа.</summary>
public class StairsTest
{
    /// <summary>Мир стартовой комнаты core.</summary>
    private static readonly World _world = StarterRoom().World;

    /// <summary>Снизу вверх по лестнице - на второй этаж, там до северной стены.</summary>
    [Fact]
    public void ClimbUp()
    {
        WorldPos end = Walk(new WorldPos(4.5, 7.5, 0), new MoveInput(0, -1), seconds: 3);

        Assert.Equal(1, end.Z);
        Assert.Equal(4 + MoveParams.Default.Radius, end.Y, 6);
    }

    /// <summary>С выхода второго этажа в проём - вниз по лестнице и дальше до южной стены первого этажа.</summary>
    [Fact]
    public void ClimbDown()
    {
        WorldPos end = Walk(new WorldPos(4.5, 4.5, 1), new MoveInput(0, 1), seconds: 4);

        Assert.Equal(0, end.Z);
        Assert.Equal(9 - MoveParams.Default.Radius, end.Y, 6);
    }

    /// <summary>Наверху проём над лестницей держит, как перила; внизу шаг на лестницу сбоку этаж не меняет.</summary>
    [Fact]
    public void HoleHoldsAndSideStepStays()
    {
        WorldPos upstairs = Walk(new WorldPos(5.5, 6.5, 1), new MoveInput(-1, 0), seconds: 2);
        WorldPos downstairs = Walk(new WorldPos(5.5, 6.5, 0), new MoveInput(-1, 0), seconds: 2);

        Assert.Equal((1, 5 + MoveParams.Default.Radius), (upstairs.Z, System.Math.Round(upstairs.X, 6)));
        Assert.Equal(0, downstairs.Z);
        Assert.True(downstairs.X < 5, "walked onto the stairs from the side");
    }

    /// <summary>Подъём для картинки: доля пути от нижнего края лестницы, вне лестницы - 0.</summary>
    [Fact]
    public void RiseAlongStairs()
    {
        Assert.Equal(0.5 / 3, Stairs.Rise(_world, new WorldPos(4.5, 7.5, 0)), 6);
        Assert.Equal(2.9 / 3, Stairs.Rise(_world, new WorldPos(4.5, 5.1, 0)), 6);
        Assert.Equal(0, Stairs.Rise(_world, new WorldPos(6.5, 6.5, 0)));
    }

    /// <summary>Путь на второй этаж идёт по лестнице: с её верха на выход этажом выше.</summary>
    [Fact]
    public void PathGoesUpTheStairs()
    {
        List<CellPos> path = [];

        bool found = new Pathfinder().Find(_world, new CellPos(6, 6, 0), new CellPos(7, 6, 1), path);
        int up = path.IndexOf(new CellPos(4, 4, 1));

        Assert.True(found, "path exists");
        Assert.True(up > 0 && path[up - 1] == new CellPos(4, 5, 0), "up from the top of the stairs");
    }

    /// <summary>Зомби видел, как человек ушёл наверх, и идёт за ним по лестнице; через перекрытие не бьёт.</summary>
    [Fact]
    public void ZombieFollowsUpstairs()
    {
        Simulation sim = new(ModLoader.Load(RepoMods.Root, ["core", "zombies"]), new ListLog(), "core:starter_room");
        long human = sim.CreateEntity(new WorldPos(4.5, 6.5, 0), "core:human");
        long zombie = sim.CreateEntity(new WorldPos(6.5, 6.5, 0), "zombies:zombie");
        int health = sim.StatTable.StatIndex("core:health");

        for (int i = 0; i < 25; i++)
        {
            sim.Entity(human).Input = new MoveInput(0, -1);
            sim.Tick();
        }

        // Человек ушёл к северо-восточному углу второго этажа и ждёт
        sim.Entity(human).Input = MoveInput.None;
        bool hitThroughFloor = false;
        for (int i = 0; i < 150 && sim.Entity(zombie).Position.Z == 0; i++)
        {
            sim.Tick();
            hitThroughFloor |= sim.Entity(human).Stats.Value(health) < 100;
        }

        Assert.Equal(1, sim.Entity(human).Position.Z);
        Assert.Equal(1, sim.Entity(zombie).Position.Z);
        Assert.False(hitThroughFloor, "no hits through the floor");
    }

    /// <summary>Цель прямо над зомби, этажом выше, в дальности удара по плоскости: удара нет, и зомби не стоит под ней, а идёт к лестнице.</summary>
    [Fact]
    public void NoHitThroughFloor()
    {
        Simulation sim = new(ModLoader.Load(RepoMods.Root, ["core", "zombies"]), new ListLog(), "core:starter_room");
        long human = sim.CreateEntity(new WorldPos(6.5, 6.5, 0), "core:human");
        long zombie = sim.CreateEntity(new WorldPos(7.3, 6.5, 0), "zombies:zombie");
        sim.Tick();

        sim.Entity(human).Position = new WorldPos(7.3, 6.5, 1);
        for (int i = 0; i < 20; i++)
        {
            sim.Tick();
        }

        WorldPos at = sim.Entity(zombie).Position;
        Assert.Equal(100, sim.Entity(human).Stats.Value(sim.StatTable.StatIndex("core:health")));
        Assert.Equal(human, sim.Entity(zombie).Brain!.Target?.Id);
        Assert.True(at.X < 6.5, $"zombie walks towards the stairs, now at {at}");
    }

    /// <summary>Своё движение у клиента по лестнице совпадает с сервером, и этаж тоже.</summary>
    [Fact]
    public void PredictionMatchesOnStairs()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        for (int i = 0; i < 14; i++)
        {
            client.SendInput(new MoveInput(-1, 0));
            run.Tick(1, client);
        }

        for (int i = 0; i < 25; i++)
        {
            client.SendInput(new MoveInput(0, -1));
            run.Tick(1, client);
        }

        client.SendInput(MoveInput.None);
        run.Tick(3, client);
        WorldPos server = run.Sim.Entity(client.Replica.OwnEntityId).Position;

        Assert.Equal(1, server.Z);
        Assert.Equal(server.Z, client.OwnPosition!.Value.Z);
        Assert.Equal(server.X, client.OwnPosition.Value.X, 4);
        Assert.Equal(server.Y, client.OwnPosition.Value.Y, 4);
    }

    /// <summary>Тело идёт с вводом input seconds секунд.</summary>
    private static WorldPos Walk(WorldPos from, MoveInput input, double seconds)
    {
        WorldPos position = from;
        for (int i = 0; i < (int)System.Math.Round(seconds / GameClock.RealSecondsPerTick); i++)
        {
            position = Movement.Step(_world, position, input, GameClock.RealSecondsPerTick, MoveParams.Default);
        }

        return position;
    }

    /// <summary>Стартовая комната core.</summary>
    private static Simulation StarterRoom()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        return new Simulation(mods.Load("core"), new ListLog(), "core:starter_room");
    }
}
