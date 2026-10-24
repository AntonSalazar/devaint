using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Controls;

/// <summary>Действия управления по номерам для сети; строится один раз после загрузки модов.</summary>
public sealed class ControlTable
{
    /// <summary>Действия по номерам.</summary>
    private readonly IReadOnlyList<ControlDef> _controls;

    /// <summary>Номер действия по id.</summary>
    private readonly Dictionary<string, int> _index = [];

    /// <summary>Таблица по проверенным определениям.</summary>
    public ControlTable(DefDatabase defs)
    {
        _controls = defs.All<ControlDef>();
        for (int i = 0; i < _controls.Count; i++)
        {
            _index[_controls[i].Id] = i;
        }
    }

    /// <summary>Число действий.</summary>
    public int Count => _controls.Count;

    /// <summary>Действие по номеру.</summary>
    public ControlDef Control(int index) => _controls[index];

    /// <summary>Номер действия по id, исключение если его нет.</summary>
    public int Index(string id) => _index[id];
}
