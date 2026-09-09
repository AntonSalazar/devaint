using System;
using System.Linq;

using DevAInt.Sim.Data;

namespace DevAInt.Sim.Tool;


/// <summary>
/// Точка входа консольных инструментов симуляции.
/// Использование: <c>map &lt;data-dir&gt; &lt;seed&gt; &lt;factions&gt;</c>.
/// </summary>
public static class Program
{
    /// <summary>
    /// Метод входа: разбирает команду и запускает инструмент.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Код выхода: 0 - успех, 1 - неверные аргументы.</returns>
    public static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "map")
        {
            DumpMap(args[1], int.Parse(args[2]), int.Parse(args[3]));
            return 0;
        }

        Console.Error.WriteLine("usage: map <data-dir> <seed> <factions>");
        return 1;
    }


    /// <summary>
    /// Метод печати сгенерированной карты в консоль.
    /// </summary>
    /// <param name="dataDir">Папка с JSON-данными.</param>
    /// <param name="seed">Сид.</param>
    /// <param name="factions">Количество фракций.</param>
    private static void DumpMap(string dataDir, int seed, int factions)
    {
        Rules rules = Rules.Load(dataDir);
        MapResult result = MapGen.Generate(rules, seed, factions);
        World world = result.World;

        Console.WriteLine(MapGen.Dump(world, result.Starts));
        Console.WriteLine($"seed {result.SeedUsed} (asked {seed}), factions {factions}, map {world.Width}x{world.Height}");
        Console.WriteLine($"nodes {world.Nodes().Count()} / {world.Width * world.Height}");
        Console.WriteLine("legend: . empty  h home  w workstation  S server  R router  i iot  C controller  * start");
        foreach (Link link in world.Links)
        {
            Console.WriteLine($"link {link.Kind}: {link.A.ToOffset()} - {link.B.ToOffset()}");
        }
    }
}
