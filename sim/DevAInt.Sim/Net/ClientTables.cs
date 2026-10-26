using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Controls;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Net;

/// <summary>Таблицы номеров клиента и его моды для сверки с сервером.</summary>
public sealed record ClientTables(StatTable Stats, ItemTable Items, CreatureTable Creatures, ControlTable Controls, IReadOnlyList<ModInfo> Mods, WorldTable World)
{
    /// <summary>Таблицы по загруженным модам, для клиента без своего sim.</summary>
    public static ClientTables From(ModLoadResult mods) => new(new StatTable(mods.Defs), new ItemTable(mods.Defs), new CreatureTable(mods.Defs), new ControlTable(mods.Defs), [.. mods.Mods.Select(mod => mod.Info)], new WorldTable(mods.Defs));

    /// <summary>Те же таблицы, что у sim, для клиента рядом с сервером и тестов.</summary>
    public static ClientTables From(Simulation sim) => new(sim.StatTable, sim.ItemTable, sim.CreatureTable, sim.ControlTable, sim.Mods, sim.WorldTable);
}
