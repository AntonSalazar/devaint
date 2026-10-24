using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Creatures;

/// <summary>Виды существ по номерам для сети; строится один раз после загрузки модов.</summary>
public sealed class CreatureTable
{
    /// <summary>Виды по номерам.</summary>
    private readonly IReadOnlyList<CreatureDef> _creatures;

    /// <summary>Номер вида по id.</summary>
    private readonly Dictionary<string, int> _index = [];

    /// <summary>Таблица по проверенным определениям.</summary>
    public CreatureTable(DefDatabase defs)
    {
        _creatures = defs.All<CreatureDef>();
        for (int i = 0; i < _creatures.Count; i++)
        {
            _index[_creatures[i].Id] = i;
        }
    }

    /// <summary>Число видов.</summary>
    public int Count => _creatures.Count;

    /// <summary>Вид по номеру.</summary>
    public CreatureDef Creature(int index) => _creatures[index];

    /// <summary>Номер вида по id, исключение если его нет.</summary>
    public int Index(string id) => _index[id];

    /// <summary>Номер вида по id, false если его нет.</summary>
    public bool TryIndex(string id, out int index) => _index.TryGetValue(id, out index);
}
