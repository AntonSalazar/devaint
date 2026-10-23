using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace DevAInt.Sim.Defs;

/// <summary>Итоговые определения всех модов после патчей.</summary>
public sealed class DefDatabase
{
    /// <summary>Определения по id.</summary>
    private readonly Dictionary<string, IDef> _defs = [];

    /// <summary>Число определений.</summary>
    public int Count => _defs.Count;

    /// <summary>Определение по id, исключение если его нет или тип другой.</summary>
    public T Get<T>(string id)
        where T : IDef => (T)_defs[id];

    /// <summary>Определение по id, false если его нет или тип другой.</summary>
    public bool TryGet<T>(string id, [MaybeNullWhen(false)] out T def)
        where T : IDef
    {
        if (_defs.TryGetValue(id, out IDef? found) && found is T typed)
        {
            def = typed;
            return true;
        }

        def = default;
        return false;
    }

    /// <summary>Все определения типа, по id.</summary>
    public IReadOnlyList<T> All<T>()
        where T : IDef
    {
        IEnumerable<T> defs = _defs.Values.OfType<T>();

        // Сортировка нужна для одинакового порядка на сервере и в тестах; вызывается при запуске, не каждый тик
        return [.. defs.OrderBy(def => def.Id, StringComparer.Ordinal)];
    }

    /// <summary>Добавление определения при загрузке модов.</summary>
    internal void Add(IDef def) => _defs.Add(def.Id, def);
}
