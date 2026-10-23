using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Items;

/// <summary>Предметы и действия по номерам для сети; строится один раз после загрузки модов.</summary>
public sealed class ItemTable
{
    /// <summary>Предметы по номерам.</summary>
    private readonly IReadOnlyList<ItemDef> _items;

    /// <summary>Действия по номерам.</summary>
    private readonly IReadOnlyList<ActionDef> _actions;

    /// <summary>Номер предмета по id.</summary>
    private readonly Dictionary<string, int> _itemIndex = [];

    /// <summary>Номер действия по id.</summary>
    private readonly Dictionary<string, int> _actionIndex = [];

    /// <summary>Таблица по проверенным определениям.</summary>
    public ItemTable(DefDatabase defs)
    {
        _items = defs.All<ItemDef>();
        _actions = defs.All<ActionDef>();
        for (int i = 0; i < _items.Count; i++)
        {
            _itemIndex[_items[i].Id] = i;
        }

        for (int i = 0; i < _actions.Count; i++)
        {
            _actionIndex[_actions[i].Id] = i;
        }
    }

    /// <summary>Число предметов.</summary>
    public int ItemCount => _items.Count;

    /// <summary>Число действий.</summary>
    public int ActionCount => _actions.Count;

    /// <summary>Предмет по номеру.</summary>
    public ItemDef Item(int index) => _items[index];

    /// <summary>Действие по номеру.</summary>
    public ActionDef Action(int index) => _actions[index];

    /// <summary>Номер предмета по id, исключение если его нет.</summary>
    public int ItemIndex(string id) => _itemIndex[id];

    /// <summary>Номер действия по id, исключение если его нет.</summary>
    public int ActionIndex(string id) => _actionIndex[id];
}
