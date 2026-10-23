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
        ["floors"] = typeof(FloorDef),
        ["walls"] = typeof(WallDef),
        ["maps"] = typeof(MapDef),
    };

    /// <summary>Статы.</summary>
    public List<StatDef?>? Stats { get; init; }

    /// <summary>Полы клеток.</summary>
    public List<FloorDef?>? Floors { get; init; }

    /// <summary>Стены и двери на рёбрах.</summary>
    public List<WallDef?>? Walls { get; init; }

    /// <summary>Карты.</summary>
    public List<MapDef?>? Maps { get; init; }

    /// <summary>Патчи определений этого и других модов.</summary>
    public List<PatchDef?>? Patches { get; init; }
}
