using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Mods;

/// <summary>Содержимое одного файла data/*.jsonc: списки определений по категориям и патчи.</summary>
internal sealed class DefFile
{
    /// <summary>Категории определений: имя списка в файле и тип записи.</summary>
    public static readonly IReadOnlyDictionary<string, Type> Categories = new Dictionary<string, Type>
    {
        ["stats"] = typeof(StatDef),
    };

    /// <summary>Статы.</summary>
    public List<StatDef?>? Stats { get; init; }

    /// <summary>Патчи определений этого и других модов.</summary>
    public List<PatchDef?>? Patches { get; init; }
}
