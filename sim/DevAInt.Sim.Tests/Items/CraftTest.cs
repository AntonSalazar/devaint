using System.IO;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Saves;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using DevAInt.Sim.Tests.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Items;

/// <summary>Тесты крафта: рецепт тратит входы и даёт выходы через действие с длительностью, все отказы, прерывания, сохранение и сеть.</summary>
public class CraftTest
{
    /// <summary>Неверный рецепт - понятная ошибка загрузки.</summary>
    [Theory]
    [InlineData("""{ "id": "test:r", "outputs": [], "seconds": 1 }""", "outputs must not be empty")]
    [InlineData("""{ "id": "test:r", "outputs": [ { "item": "core:plank" } ], "seconds": 0 }""", "seconds 0 must be positive")]
    [InlineData("""{ "id": "test:r", "outputs": [ { "item": "core:nope" } ], "seconds": 1 }""", "outputs[0]: unknown item core:nope")]
    [InlineData("""{ "id": "test:r", "inputs": [ { "item": "core:log", "count": 0 } ], "outputs": [ { "item": "core:plank" } ], "seconds": 1 }""", "inputs[0]: count 0 must be at least 1")]
    [InlineData("""{ "id": "test:r", "inputs": [ { "item": "core:saw" } ], "tools": ["core:saw"], "outputs": [ { "item": "core:plank" } ], "seconds": 1 }""", "tools: core:saw is also an input")]
    [InlineData("""{ "id": "test:r", "outputs": [ { "item": "core:plank" } ], "seconds": 1, "action": "core:fly" }""", "unknown action core:fly")]
    public void BadRecipeIsError(string recipe, string message)
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("test", depends: "core").Write("test/data/recipes.jsonc", $$"""{ "recipes": [ {{recipe}} ] }""");

        ModError error = Assert.Single(mods.Load("core", "test").Errors);

        Assert.Equal(message, error.Message);
    }

    /// <summary>Бревно и пила - через 6 секунд три доски, пила остаётся, бревна нет.</summary>
    [Fact]
    public void SawLogIntoPlanks()
    {
        (Simulation sim, long entity) = Crafter("core:log", "core:saw");

        ActionStart start = sim.StartCraft(entity, "core:saw_log");
        int ticks = TicksUntilDone(sim, entity);

        Assert.Equal(ActionStart.Started, start);
        Assert.Equal(60, ticks);
        Assert.Equal(ActionEnd.Completed, sim.Entity(entity).LastActionEnd);
        Assert.Equal(["core:plank", "core:plank", "core:plank", "core:saw"], Items(sim, entity).Order());
    }

    /// <summary>Без пилы, без рецепта, во время другого действия и с перегрузом крафт не начинается.</summary>
    [Fact]
    public void CraftRefusals()
    {
        (Simulation noSaw, long alone) = Crafter("core:log");
        (Simulation busy, long worker) = Crafter("core:log", "core:saw");
        busy.StartCraft(worker, "core:saw_log");
        (Simulation heavy, long loaded) = Crafter("core:plank", "core:plank", "core:plank");
        for (int i = 0; i < 12; i++)
        {
            heavy.GiveItem(loaded, heavy.ItemTable.Item(heavy.ItemTable.ItemIndex("core:plank")));
        }

        Assert.Equal(ActionStart.NoItem, noSaw.StartCraft(alone, "core:saw_log"));
        Assert.Equal(ActionStart.NoUse, noSaw.StartCraft(alone, "core:sofa"));
        Assert.Equal(ActionStart.Busy, busy.StartCraft(worker, "core:saw_log"));
        Assert.Equal(ActionStart.Full, heavy.StartCraft(loaded, "test:glue_planks"));
    }

    /// <summary>Движение прерывает крафт; бревно, ушедшее в ящик посреди крафта, тоже - досок нет.</summary>
    [Fact]
    public void CraftIsInterrupted()
    {
        (Simulation walker, long moving) = Crafter("core:log", "core:saw");
        walker.StartCraft(moving, "core:saw_log");
        walker.Entity(moving).Input = new MoveInput(1, 0);
        walker.Tick();
        (Simulation sim, long entity) = Crafter("core:log", "core:saw");
        sim.StartCraft(entity, "core:saw_log");
        long crate = sim.World.ObjectAt(new CellPos(9, 8, 0))!.Id;
        long log = sim.Entity(entity).Inventory.Items.First(item => item.Def.Id == "core:log").Id;
        Assert.Equal(ItemMove.Moved, sim.MoveItem(entity, log, 0, crate));
        TicksUntilDone(sim, entity);

        Assert.Null(walker.Entity(moving).Action);
        Assert.Equal(ActionEnd.Interrupted, walker.Entity(moving).LastActionEnd);
        Assert.Equal(ActionEnd.Interrupted, sim.Entity(entity).LastActionEnd);
        Assert.DoesNotContain("core:plank", Items(sim, entity));
    }

    /// <summary>Крафт, начатый до сохранения, после загрузки идёт дальше и заканчивается досками.</summary>
    [Fact]
    public void CraftSurvivesSave()
    {
        using TempMods mods = Mods();
        ModLoadResult loaded = mods.Load("core", "test");
        Simulation sim = new(loaded, new ListLog(), "core:starter_room");
        long entity = sim.CreateEntity(new WorldPos(8.5, 8.5, 0), "core:human");
        Give(sim, entity, "core:log", "core:saw");
        sim.StartCraft(entity, "core:saw_log");
        sim.Tick();
        string dir = Path.Combine(Path.GetDirectoryName(mods.Root)!, "save");
        new SaveGame(dir).Save(sim);

        Simulation restored = new SaveGame(dir).Load(mods.Load("core", "test"), new ListLog());
        bool crafting = restored.Entity(entity).Action?.Recipe?.Id == "core:saw_log";
        TicksUntilDone(restored, entity);

        Assert.True(crafting, "recipe action is restored");
        Assert.Equal(3, Items(restored, entity).Count(item => item == "core:plank"));
    }

    /// <summary>По сети: клиент просит крафт, видит начало действия, а потом доски в своём инвентаре.</summary>
    [Fact]
    public void CraftOverNetwork()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        Give(run.Sim, client.Replica.OwnEntityId, "core:log", "core:saw");
        run.Tick(1, client);

        client.SendStartCraft(Index(run.Sim, "core:saw_log"));
        run.Tick(1, client);
        bool started = client.Replica.Entity(client.Replica.OwnEntityId)!.Action is { Verb: "core:saw_log", Subject: "" };
        run.Tick(61, client);

        Assert.True(started, "client sees the action with the recipe label");
        Assert.Equal(3, client.Replica.Inventory.Count(item => run.Sim.ItemTable.Item(item.Item).Id == "core:plank"));
    }

    /// <summary>Номер рецепта по id.</summary>
    private static int Index(Simulation sim, string recipe) => sim.ItemTable.TryRecipeIndex(recipe, out int index) ? index : -1;

    /// <summary>Тики до конца действия сущности, не больше 500.</summary>
    private static int TicksUntilDone(Simulation sim, long entity)
    {
        int ticks = 0;
        while (sim.Entity(entity).Action is not null && ticks < 500)
        {
            sim.Tick();
            ticks++;
        }

        return ticks;
    }

    /// <summary>Id видов предметов инвентаря сущности.</summary>
    private static string[] Items(Simulation sim, long entity) => [.. sim.Entity(entity).Inventory.Items.Select(item => item.Def.Id)];

    /// <summary>Предметы по id в инвентарь сущности.</summary>
    private static void Give(Simulation sim, long entity, params string[] items)
    {
        foreach (string item in items)
        {
            sim.GiveItem(entity, sim.ItemTable.Item(sim.ItemTable.ItemIndex(item)));
        }
    }

    /// <summary>Человек у ящика стартовой комнаты с предметами; есть и тяжёлый рецепт test:glue_planks.</summary>
    private static (Simulation Sim, long Entity) Crafter(params string[] items)
    {
        using TempMods mods = Mods();
        Simulation sim = new(mods.Load("core", "test"), new ListLog(), "core:starter_room");
        long entity = sim.CreateEntity(new WorldPos(8.5, 8.5, 0), "core:human");
        Give(sim, entity, items);
        return (sim, entity);
    }

    /// <summary>Моды core и test: в test рецепт, где три доски склеиваются в два бревна по 4 кг.</summary>
    private static TempMods Mods()
    {
        TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("test", depends: "core").Write("test/data/recipes.jsonc", """
            { "recipes": [ { "id": "test:glue_planks", "inputs": [ { "item": "core:plank", "count": 3 } ],
                             "outputs": [ { "item": "core:log", "count": 2 } ], "seconds": 1 } ] }
            """);
        return mods;
    }
}
