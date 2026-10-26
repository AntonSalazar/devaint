using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Items;

/// <summary>Экземпляр предмета: свой id, чтобы ссылка на предмет не ломалась при изменении инвентаря.</summary>
public sealed record Item(long Id, ItemDef Def);

/// <summary>Предметы сущности или контейнера: простой список без стопок, вместимость проверяет тот, кто кладёт.</summary>
public sealed class Inventory
{
    /// <summary>Предметы в порядке появления.</summary>
    private readonly List<Item> _items = [];

    /// <summary>Предметы в порядке появления.</summary>
    public IReadOnlyList<Item> Items => _items;

    /// <summary>Общий вес предметов в килограммах; список короткий, поэтому считается заново.</summary>
    public double Weight
    {
        get
        {
            double weight = 0;
            foreach (Item item in _items)
            {
                weight += item.Def.Weight;
            }

            return weight;
        }
    }

    /// <summary>Номер изменения, растёт при каждом изменении: по нему сервер понимает, что пора отправить инвентарь.</summary>
    public int Version { get; private set; }

    /// <summary>Добавление предмета.</summary>
    public void Add(Item item)
    {
        _items.Add(item);
        Version++;
    }

    /// <summary>Предмет по id или null.</summary>
    public Item? Find(long id) => _items.Find(item => item.Id == id);

    /// <summary>Удаление предмета по id, false если его нет.</summary>
    public bool Remove(long id)
    {
        if (_items.RemoveAll(item => item.Id == id) == 0)
        {
            return false;
        }

        Version++;
        return true;
    }
}
