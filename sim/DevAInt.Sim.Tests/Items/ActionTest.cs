using System.Linq;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Items;

/// <summary>Тесты действий с предметами на моде core: core:eat и core:apple.</summary>
public class ActionTest
{
    /// <summary>Тиков на 4 секунды еды при обычной скорости времени.</summary>
    private const int EatTicks = 40;

    /// <summary>Игрок появляется с тремя яблоками из точки появления карты.</summary>
    [Fact]
    public void PlayerSpawnsWithApples()
    {
        (Simulation sim, Entity player) = Start();

        Assert.Equal(3, player.Inventory.Items.Count);
        Assert.All(player.Inventory.Items, item => Assert.Equal("core:apple", item.Def.Id));
        Assert.Equal(3, player.Inventory.Items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(sim.Spawn, player.Position);
    }

    /// <summary>Итог этапа 9: съел яблоко - голод упал на 25, яблоко исчезло.</summary>
    [Fact]
    public void EatingAppleLowersHungerAndConsumesIt()
    {
        (Simulation sim, Entity player) = Start();
        int hunger = sim.StatTable.StatIndex("core:hunger");
        player.Stats.SetValue(hunger, 60);
        Item apple = player.Inventory.Items[0];

        Assert.Equal(ActionStart.Started, sim.StartAction(player.Id, apple.Id, "core:eat"));
        Tick(sim, EatTicks - 1);
        Assert.NotNull(player.Action);
        Assert.Equal(3, player.Inventory.Items.Count);

        sim.Tick();

        Assert.Null(player.Action);
        Assert.Equal(ActionEnd.Completed, player.LastActionEnd);
        Assert.Equal(2, player.Inventory.Items.Count);
        Assert.Null(player.Inventory.Find(apple.Id));
        // Пока ел, голод рос с обычной скоростью
        double grown = EatTicks * sim.Clock.MinutesPerTick * sim.StatTable.Stat(hunger).Rate;
        Assert.Equal(60 + grown - 25, player.Stats.Value(hunger), 6);
    }

    /// <summary>Итог этапа 9: движение прерывает еду, яблоко и голод остаются.</summary>
    [Fact]
    public void MovingInterruptsEating()
    {
        (Simulation sim, Entity player) = Start();
        int hunger = sim.StatTable.StatIndex("core:hunger");
        player.Stats.SetValue(hunger, 60);
        sim.StartAction(player.Id, player.Inventory.Items[0].Id, "core:eat");
        Tick(sim, 10);

        player.Input = new MoveInput(1, 0);
        sim.Tick();

        Assert.Null(player.Action);
        Assert.Equal(ActionEnd.Interrupted, player.LastActionEnd);
        Assert.Equal(3, player.Inventory.Items.Count);
        Assert.True(player.Stats.Value(hunger) > 59, $"hunger {player.Stats.Value(hunger)} dropped although eating was interrupted");
    }

    /// <summary>Крошечный ввод от дрожащего стика не прерывает действие.</summary>
    [Fact]
    public void TinyInputDoesNotInterrupt()
    {
        (Simulation sim, Entity player) = Start();
        sim.StartAction(player.Id, player.Inventory.Items[0].Id, "core:eat");

        player.Input = new MoveInput(0.005, 0);
        Tick(sim, EatTicks);

        Assert.Equal(ActionEnd.Completed, player.LastActionEnd);
    }

    /// <summary>Отмена прерывает действие.</summary>
    [Fact]
    public void CancelStopsAction()
    {
        (Simulation sim, Entity player) = Start();
        sim.StartAction(player.Id, player.Inventory.Items[0].Id, "core:eat");

        Assert.True(sim.CancelAction(player.Id));
        Assert.False(sim.CancelAction(player.Id));
        Tick(sim, EatTicks);

        Assert.Equal(3, player.Inventory.Items.Count);
        Assert.Equal(ActionEnd.Interrupted, player.LastActionEnd);
    }

    /// <summary>Нельзя начать второе действие, действие с чужим предметом или без такого применения.</summary>
    [Fact]
    public void StartChecks()
    {
        (Simulation sim, Entity player) = Start();
        long apple = player.Inventory.Items[0].Id;
        long other = sim.SpawnPlayer();

        Assert.Equal(ActionStart.NoItem, sim.StartAction(player.Id, sim.Entity(other).Inventory.Items[0].Id, "core:eat"));
        Assert.Equal(ActionStart.NoItem, sim.StartAction(player.Id, 999, "core:eat"));
        Assert.Equal(ActionStart.NoUse, sim.StartAction(player.Id, apple, "core:drink"));
        Assert.Equal(ActionStart.Started, sim.StartAction(player.Id, apple, "core:eat"));
        Assert.Equal(ActionStart.Busy, sim.StartAction(player.Id, player.Inventory.Items[1].Id, "core:eat"));
    }

    /// <summary>Ускорение времени ускоряет и действия: при ускорении 10 еда занимает 4 тика.</summary>
    [Fact]
    public void TimeScaleSpeedsUpActions()
    {
        (Simulation sim, Entity player) = Start(timeScale: 10);
        sim.StartAction(player.Id, player.Inventory.Items[0].Id, "core:eat");

        Assert.Equal(4, player.Action!.DurationTicks);
        Tick(sim, 4);

        Assert.Equal(ActionEnd.Completed, player.LastActionEnd);
    }

    /// <summary>Sim с core, стартовой комнатой и игроком.</summary>
    private static (Simulation Sim, Entity Player) Start(double timeScale = 1)
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Simulation sim = new(result, new ListLog(), "core:starter_room", new Sim.Stats.GameClock(timeScale));
        return (sim, sim.Entity(sim.SpawnPlayer()));
    }

    /// <summary>Несколько тиков.</summary>
    private static void Tick(Simulation sim, int count)
    {
        for (int i = 0; i < count; i++)
        {
            sim.Tick();
        }
    }
}
