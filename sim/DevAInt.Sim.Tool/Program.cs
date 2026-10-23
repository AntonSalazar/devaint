using System;
using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;

namespace DevAInt.Sim.Tool;

/// <summary>Отладочные команды sim в терминале.</summary>
internal static class Program
{
    /// <summary>Справка по командам.</summary>
    private const string Usage = "usage: map <mods dir> <map id> [z] [mod ids...]";

    /// <summary>Запуск команды, код выхода 0 при успехе.</summary>
    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "map")
        {
            return Map(args[1], args[2], [.. args.Skip(3)]);
        }

        Console.Error.WriteLine(Usage);
        return 2;
    }

    /// <summary>ASCII-дамп этажа карты; моды по умолчанию - только core.</summary>
    private static int Map(string modsDir, string mapId, string[] rest)
    {
        int z = rest.Length > 0 && int.TryParse(rest[0], out int level) ? level : 0;
        string[] mods = rest.Length > 1 ? rest[1..] : ["core"];
        ModLoadResult result = ModLoader.Load(modsDir, mods);
        if (!result.Ok)
        {
            foreach (ModError error in result.Errors)
            {
                Console.Error.WriteLine(error);
            }

            return 1;
        }

        if (!result.Defs.TryGet<MapDef>(mapId, out MapDef? map))
        {
            Console.Error.WriteLine($"map {mapId} not found");
            return 1;
        }

        WorldTable table = new(result.Defs);
        Console.Write(WorldDump.Ascii(MapBuilder.Build(map, table), table, z));
        return 0;
    }
}
