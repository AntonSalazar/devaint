using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Items;

/// <summary>Предметы, действия и рецепты по номерам для сети; строится один раз после загрузки модов.</summary>
public sealed class ItemTable
{
    /// <summary>Предметы по номерам.</summary>
    private readonly IReadOnlyList<ItemDef> _items;

    /// <summary>Действия по номерам.</summary>
    private readonly IReadOnlyList<ActionDef> _actions;

    /// <summary>Рецепты по номерам.</summary>
    private readonly IReadOnlyList<RecipeDef> _recipes;

    /// <summary>Номер рецепта по id.</summary>
    private readonly Dictionary<string, int> _recipeIndex = [];

    /// <summary>Номер предмета по id.</summary>
    private readonly Dictionary<string, int> _itemIndex = [];

    /// <summary>Номер действия по id.</summary>
    private readonly Dictionary<string, int> _actionIndex = [];

    /// <summary>Таблица по проверенным определениям.</summary>
    public ItemTable(DefDatabase defs)
    {
        _items = defs.All<ItemDef>();
        _actions = defs.All<ActionDef>();
        _recipes = defs.All<RecipeDef>();
        for (int i = 0; i < _recipes.Count; i++)
        {
            _recipeIndex[_recipes[i].Id] = i;
        }

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

    /// <summary>Число рецептов.</summary>
    public int RecipeCount => _recipes.Count;

    /// <summary>Рецепт по номеру.</summary>
    public RecipeDef Recipe(int index) => _recipes[index];

    /// <summary>Номер рецепта по id, false если его нет.</summary>
    public bool TryRecipeIndex(string id, out int index) => _recipeIndex.TryGetValue(id, out index);

    /// <summary>Предмет по номеру.</summary>
    public ItemDef Item(int index) => _items[index];

    /// <summary>Действие по номеру.</summary>
    public ActionDef Action(int index) => _actions[index];

    /// <summary>Номер предмета по id, исключение если его нет.</summary>
    public int ItemIndex(string id) => _itemIndex[id];

    /// <summary>Номер действия по id, исключение если его нет.</summary>
    public int ActionIndex(string id) => _actionIndex[id];

    /// <summary>Номер предмета по id, false если его нет.</summary>
    public bool TryItemIndex(string id, out int index) => _itemIndex.TryGetValue(id, out index);

    /// <summary>Номер действия по id, false если его нет.</summary>
    public bool TryActionIndex(string id, out int index) => _actionIndex.TryGetValue(id, out index);
}
