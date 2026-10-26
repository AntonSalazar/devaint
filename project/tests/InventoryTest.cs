using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;

/// <summary>Тесты окна инвентаря: стопки и вес, ближние контейнеры, щелчки перекладывают через сервер, меню применений.</summary>
public class InventoryTest : TestCase
{
    /// <summary>Клетка ящика стартовой комнаты.</summary>
    private static readonly CellPos _crateCell = new(9, 8, 0);

    /// <summary>Одинаковые предметы собраны в одну стопку с общим весом, строка и заголовок показывают число и вес.</summary>
    public void TestStacksAndText()
    {
        GameFixture game = new();
        int apple = game.Sim.ItemTable.ItemIndex("core:apple");

        List<ItemStack> stacks = InventoryModel.Stacks([new ItemData(5, apple), new ItemData(9, apple)], game.Sim.ItemTable);
        InventoryPanel panel = new(0, "Bag", stacks, 0.4, 15);

        CheckEqual(1, stacks.Count, "one stack");
        CheckEqual("5 9", string.Join(" ", stacks[0].Ids), "ids in order");
        CheckEqual($"{HudModel.Name("core:apple")} x2  0.4", InventoryWindow.Row(stacks[0]), "row");
        CheckEqual("Bag  0.4 / 15", InventoryWindow.Header(panel), "header with capacity");
        CheckEqual("Bag  0.4", InventoryWindow.Header(panel with { Capacity = double.PositiveInfinity }), "header without capacity");
    }

    /// <summary>У ящика ближний контейнер - он; после подписки его содержимое видно панелью с вместимостью.</summary>
    public void TestNearbyCrate()
    {
        GameFixture game = new();
        game.Tick();
        Entity own = NearCrate(game);
        game.Tick();
        World world = game.Client.Replica.World!;

        List<WorldObject> nearby = InventoryModel.Nearby(world, own.Position);
        game.Client.SendWatchContainers([.. nearby.Select(obj => obj.Id)]);
        game.Tick();
        List<InventoryPanel> panels = InventoryModel.NearbyPanels(game.Client.Replica, ClientTables.From(game.Sim), nearby);

        CheckEqual(1, nearby.Count, "only the crate is in reach");
        CheckEqual(_crateCell, nearby[0].Cell, "crate");
        CheckEqual(1, panels.Count, "crate panel");
        CheckEqual(2, panels[0].Stacks[0].Count, "two apples");
        CheckEqual(2, panels[0].Stacks[1].Count, "two logs");
        CheckEqual(20.0, panels[0].Capacity, "crate capacity");
        CheckEqual(0, InventoryModel.Nearby(world, new WorldPos(6.5, 6.5, 0)).Count, "nothing in reach in the middle of the room");
    }

    /// <summary>Щелчок по яблоку ящика кладёт его к себе, Shift-щелчок по своей стопке - всю стопку в ящик; меню предлагает съесть.</summary>
    public void TestClicksMoveThroughServer()
    {
        GameFixture game = new();
        game.Tick();
        NearCrate(game);
        game.Tick();
        ClientTables tables = ClientTables.From(game.Sim);
        InventoryWindow window = new();
        window.Init(tables.Items);
        window.MoveRequested += game.Client.SendMoveItem;
        List<long> used = [];
        window.UseRequested += (item, _) => used.Add(item);
        List<WorldObject> nearby = InventoryModel.Nearby(game.Client.Replica.World!, game.Sim.Entity(game.Client.Replica.OwnEntityId).Position);
        game.Client.SendWatchContainers([nearby[0].Id]);
        game.Tick();

        Show(window, game, tables, nearby);
        window.PressStack(nearby[0].Id, 0, all: false);
        game.Tick();
        int own = game.Client.Replica.Inventory.Count;
        Show(window, game, tables, nearby);
        window.PressStack(0, 0, all: true);
        game.Tick();
        Show(window, game, tables, nearby);
        List<(string Text, int Action)> uses = window.Uses(0, 0);

        CheckEqual(4, own, "apple taken from the crate");
        CheckEqual(0, game.Client.Replica.Inventory.Count, "whole stack put into the crate");
        CheckEqual(7, game.Client.Replica.Containers[nearby[0].Id].Count, "crate has five apples and two logs");
        CheckEqual(nearby[0].Id, window.Selected, "crate is selected for putting");
        CheckEqual(0, uses.Count, "nothing to use in an empty inventory");
        Check(window.NearbyLines.Any(line => line.StartsWith("> ")), "selected crate is marked");
        window.Free();
    }

    /// <summary>Меню своего яблока - съесть; выбор применяет первый предмет стопки.</summary>
    public void TestUseMenu()
    {
        GameFixture game = new();
        game.Tick();
        ClientTables tables = ClientTables.From(game.Sim);
        InventoryWindow window = new();
        window.Init(tables.Items);
        List<(long Item, int Action)> used = [];
        window.UseRequested += (item, action) => used.Add((item, action));

        window.Show(InventoryModel.Own(game.Client.Replica, tables), []);
        List<(string Text, int Action)> uses = window.Uses(0, 0);
        window.Use(0, uses[0].Action);

        CheckEqual(HudModel.Name("core:eat"), uses.Single().Text, "eat");
        CheckEqual((game.Client.Replica.Inventory[0].Id, tables.Items.ActionIndex("core:eat")), used.Single(), "first apple is eaten");
        CheckEqual(0, window.Selected, "nothing nearby to put into");
        window.Free();
    }

    /// <summary>Рецепт распила готов, только когда есть и бревно, и пила; кнопка рецепта шлёт крафт, неготовая выключена.</summary>
    public void TestRecipes()
    {
        GameFixture game = new();
        game.Tick();
        ClientTables tables = ClientTables.From(game.Sim);
        bool before = InventoryModel.Recipes(game.Client.Replica, tables.Items).Single().Ready;
        long own = game.Client.Replica.OwnEntityId;
        game.Sim.GiveItem(own, tables.Items.Item(tables.Items.ItemIndex("core:log")));
        game.Sim.GiveItem(own, tables.Items.Item(tables.Items.ItemIndex("core:saw")));
        game.Tick();
        RecipeRow ready = InventoryModel.Recipes(game.Client.Replica, tables.Items).Single();
        InventoryWindow window = new();
        window.Init(tables.Items);
        List<int> crafted = [];
        window.CraftRequested += crafted.Add;

        window.ShowRecipes([ready, ready with { Index = 7, Ready = false }]);
        window.Craft(ready.Index);

        Check(!before, "not ready without a log and a saw");
        Check(ready.Ready, "ready with a log and a saw");
        CheckEqual($"{HudModel.Name("core:log")}, {HudModel.Name("core:saw")} -> {HudModel.Name("core:plank")} x3", ready.Needs, "needs line");
        CheckEqual(ready.Index, crafted.Single(), "craft request");
        CheckEqual(5, window.CraftLines.Count, "title and two rows of two lines");
        window.Free();
    }

    /// <summary>Своя сущность идёт вводом по диагонали к ящику, пока не достанет до него рукой.</summary>
    private static Entity NearCrate(GameFixture game)
    {
        Entity own = game.Sim.Entity(game.Client.Replica.OwnEntityId);
        WorldObject crate = game.Sim.World.ObjectAt(_crateCell)!;
        for (int i = 0; i < 40 && !Reach.CanReach(game.Sim.World, own.Position, crate); i++)
        {
            game.Client.SendInput(new MoveInput(1, 1));
            game.Tick();
        }

        // Стоп: следующий ввод без движения, иначе сущность ушла бы дальше по старому вводу
        game.Client.SendInput(new MoveInput(0, 0));
        return own;
    }

    /// <summary>Окно заново по реплике.</summary>
    private static void Show(InventoryWindow window, GameFixture game, ClientTables tables, List<WorldObject> nearby) =>
        window.Show(InventoryModel.Own(game.Client.Replica, tables), InventoryModel.NearbyPanels(game.Client.Replica, tables, nearby));
}
