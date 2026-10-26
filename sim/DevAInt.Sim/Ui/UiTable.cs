using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Ui;

/// <summary>Панели интерфейса модов и действия их кнопок по номерам для сети; строится один раз после загрузки модов.</summary>
public sealed class UiTable
{
    /// <summary>Действия кнопок по номерам.</summary>
    private readonly List<string> _actions = [];

    /// <summary>Номер действия по id.</summary>
    private readonly Dictionary<string, int> _actionIndex = [];

    /// <summary>Таблица по проверенным определениям.</summary>
    public UiTable(DefDatabase defs)
    {
        Panels = defs.All<UiPanelDef>();
        foreach (UiPanelDef panel in Panels)
        {
            foreach (UiRowDef row in panel.Rows)
            {
                if (row.Action is string action && !_actionIndex.ContainsKey(action))
                {
                    _actionIndex[action] = _actions.Count;
                    _actions.Add(action);
                }
            }
        }
    }

    /// <summary>Все панели по id.</summary>
    public IReadOnlyList<UiPanelDef> Panels { get; }

    /// <summary>Число действий кнопок.</summary>
    public int ActionCount => _actions.Count;

    /// <summary>Действие по номеру.</summary>
    public string Action(int index) => _actions[index];

    /// <summary>Номер действия по id, false если его нет.</summary>
    public bool TryActionIndex(string id, out int index) => _actionIndex.TryGetValue(id, out index);
}
