using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Items;

/// <summary>Проверка и трата материалов рецептов и построек: одно правило у сервера и окна.</summary>
public static class Crafting
{
    /// <summary>Сколько предметов каждого вида по их id.</summary>
    public static Dictionary<string, int> Counts(IEnumerable<string> itemIds)
    {
        Dictionary<string, int> counts = [];
        foreach (string id in itemIds)
        {
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        return counts;
    }

    /// <summary>Сколько предметов каждого вида в инвентаре.</summary>
    public static Dictionary<string, int> Counts(Inventory inventory)
    {
        Dictionary<string, int> counts = [];
        foreach (Item item in inventory.Items)
        {
            counts[item.Def.Id] = counts.GetValueOrDefault(item.Def.Id) + 1;
        }

        return counts;
    }

    /// <summary>Первый недостающий предмет: вход, которого меньше нужного, или инструмент, которого нет; null - всё есть.</summary>
    public static string? Missing(IReadOnlyList<ItemCountDef> inputs, IReadOnlyList<string> tools, IReadOnlyDictionary<string, int> counts)
    {
        foreach (ItemCountDef input in inputs)
        {
            if (counts.GetValueOrDefault(input.Item) < input.Count)
            {
                return input.Item;
            }
        }

        foreach (string tool in tools)
        {
            if (counts.GetValueOrDefault(tool) < 1)
            {
                return tool;
            }
        }

        return null;
    }

    /// <summary>Общий вес списка предметов по их определениям.</summary>
    public static double Weight(IReadOnlyList<ItemCountDef> items, ItemTable table)
    {
        double weight = 0;
        foreach (ItemCountDef entry in items)
        {
            weight += table.Item(table.ItemIndex(entry.Item)).Weight * entry.Count;
        }

        return weight;
    }

    /// <summary>Удаление входов из инвентаря: первые по порядку предметы нужного вида; входы должны быть, их проверил Missing.</summary>
    public static void Take(Inventory inventory, IReadOnlyList<ItemCountDef> inputs)
    {
        foreach (ItemCountDef input in inputs)
        {
            for (int i = 0; i < input.Count; i++)
            {
                foreach (Item item in inventory.Items)
                {
                    if (item.Def.Id == input.Item)
                    {
                        inventory.Remove(item.Id);
                        break;
                    }
                }
            }
        }
    }
}
