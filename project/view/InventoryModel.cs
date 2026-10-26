using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Net;

/// <summary>Стопка одинаковых предметов в окне инвентаря: номер вида, число, общий вес и id по порядку.</summary>
public sealed record ItemStack(int Item, string Name, int Count, double Weight, IReadOnlyList<long> Ids);

/// <summary>Панель окна инвентаря: свой инвентарь, 0, или контейнер объекта по id; заголовок, стопки, вес и вместимость.</summary>
public sealed record InventoryPanel(long Place, string Title, IReadOnlyList<ItemStack> Stacks, double Weight, double Capacity);

/// <summary>Рецепт в окне: номер в ItemTable, название, что нужно и что получится, и можно ли начать из своего инвентаря.</summary>
public sealed record RecipeRow(int Index, string Title, string Needs, bool Ready);

/// <summary>Данные окна инвентаря по реплике: стопки, вес и ближние контейнеры; ничего не рисует.</summary>
public static class InventoryModel
{
    /// <summary>Сколько ближних контейнеров окно показывает и на сколько подписывается.</summary>
    public const int MaxNearby = 8;

    /// <summary>Ключ заголовка своего инвентаря.</summary>
    public const string OwnTitle = "ui.inventory";

    /// <summary>Предметы, собранные в стопки по виду в порядке первого появления.</summary>
    public static List<ItemStack> Stacks(IReadOnlyList<ItemData> items, ItemTable table)
    {
        List<int> order = [];
        Dictionary<int, List<long>> ids = [];
        foreach (ItemData item in items)
        {
            if (!ids.TryGetValue(item.Item, out List<long>? list))
            {
                ids[item.Item] = list = [];
                order.Add(item.Item);
            }

            list.Add(item.Id);
        }

        List<ItemStack> stacks = [];
        foreach (int kind in order)
        {
            ItemDef def = table.Item(kind);
            stacks.Add(new ItemStack(kind, HudModel.Name(def.Id), ids[kind].Count, ids[kind].Count * def.Weight, ids[kind]));
        }

        return stacks;
    }

    /// <summary>Общий вес предметов.</summary>
    public static double Weight(IReadOnlyList<ItemData> items, ItemTable table)
    {
        double weight = 0;
        foreach (ItemData item in items)
        {
            weight += table.Item(item.Item).Weight;
        }

        return weight;
    }

    /// <summary>Контейнеры в досягаемости тела в точке eye, ближние первыми, не больше MaxNearby.</summary>
    public static List<WorldObject> Nearby(World world, WorldPos eye)
    {
        List<(double Distance, WorldObject Object)> found = [];
        foreach (WorldObject obj in world.Objects)
        {
            if (obj.IsContainer && Reach.DistanceTo(world, eye, obj) is double distance && distance <= Reach.Distance)
            {
                found.Add((distance, obj));
            }
        }

        // Равные расстояния упорядочены по id, чтобы порядок панелей не прыгал между кадрами
        found.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Object.Id.CompareTo(b.Object.Id));
        List<WorldObject> nearby = [];
        for (int i = 0; i < found.Count && i < MaxNearby; i++)
        {
            nearby.Add(found[i].Object);
        }

        return nearby;
    }

    /// <summary>Панель своего инвентаря с вместимостью вида своего существа.</summary>
    public static InventoryPanel Own(Replica replica, ClientTables tables)
    {
        int creature = replica.Entity(replica.OwnEntityId)?.Creature ?? -1;
        double capacity = creature >= 0 ? tables.Creatures.Creature(creature).Inventory?.Capacity ?? double.PositiveInfinity : double.PositiveInfinity;
        string title = GameLocale.Text(OwnTitle, "Inventory");
        return new InventoryPanel(0, title, Stacks(replica.Inventory, tables.Items), Weight(replica.Inventory, tables.Items), capacity);
    }

    /// <summary>Все рецепты с готовностью по своему инвентарю: то же правило Crafting.Missing, что у сервера.</summary>
    public static List<RecipeRow> Recipes(Replica replica, ItemTable table)
    {
        List<string> own = [];
        foreach (ItemData item in replica.Inventory)
        {
            own.Add(table.Item(item.Item).Id);
        }

        Dictionary<string, int> counts = Crafting.Counts(own);
        List<RecipeRow> rows = [];
        for (int i = 0; i < table.RecipeCount; i++)
        {
            RecipeDef recipe = table.Recipe(i);
            rows.Add(new RecipeRow(i, HudModel.Name(recipe.Id), Needs(recipe), Crafting.Missing(recipe.Inputs, recipe.Tools, counts) is null));
        }

        return rows;
    }

    /// <summary>Строка состава рецепта: входы и инструменты, стрелка, выходы.</summary>
    public static string Needs(RecipeDef recipe)
    {
        List<string> needs = [];
        foreach (ItemCountDef input in recipe.Inputs)
        {
            needs.Add(Counted(input));
        }

        foreach (string tool in recipe.Tools)
        {
            needs.Add(HudModel.Name(tool));
        }

        List<string> outputs = [];
        foreach (ItemCountDef output in recipe.Outputs)
        {
            outputs.Add(Counted(output));
        }

        return $"{string.Join(", ", needs)} -> {string.Join(", ", outputs)}";
    }

    /// <summary>Панели ближних контейнеров, содержимое которых уже пришло от сервера, в порядке nearby.</summary>
    public static List<InventoryPanel> NearbyPanels(Replica replica, ClientTables tables, IReadOnlyList<WorldObject> nearby)
    {
        List<InventoryPanel> panels = [];
        foreach (WorldObject obj in nearby)
        {
            if (replica.Containers.TryGetValue(obj.Id, out IReadOnlyList<ItemData>? items))
            {
                string title = HudModel.Name(tables.World.Object(obj.Def).Id);
                panels.Add(new InventoryPanel(obj.Id, title, Stacks(items, tables.Items), Weight(items, tables.Items), obj.Capacity));
            }
        }

        return panels;
    }

    /// <summary>Название предмета и число, если штук больше одной.</summary>
    private static string Counted(ItemCountDef entry) => entry.Count > 1 ? $"{HudModel.Name(entry.Item)} x{entry.Count}" : HudModel.Name(entry.Item);
}
