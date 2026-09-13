using System;
using System.Linq;

using DevAInt.Sim.Data;

namespace DevAInt.Sim.Tool;


/// <summary>
/// Точка входа консольных инструментов симуляции.
/// Использование: <c>map &lt;data-dir&gt; &lt;seed&gt;</c> или <c>play &lt;data-dir&gt; &lt;seed&gt; [mutator…]</c>.
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
        if (args.Length == 3 && args[0] == "map")
        {
            DumpMap(args[1], int.Parse(args[2]));
            return 0;
        }
        if (args.Length >= 3 && args[0] == "play")
        {
            return Play.Run(args[1], int.Parse(args[2]), args[3..]);
        }

        Console.Error.WriteLine("usage: map <data-dir> <seed> | play <data-dir> <seed> [mutator...]");
        return 1;
    }


    /// <summary>
    /// Метод печати сгенерированной карты в консоль.
    /// </summary>
    /// <param name="dataDir">Папка с JSON-данными.</param>
    /// <param name="seed">Сид.</param>
    private static void DumpMap(string dataDir, int seed)
    {
        Rules rules = Rules.Load(dataDir);
        MapResult result = MapGen.Generate(rules, seed);
        World world = result.World;

        Console.WriteLine(MapGen.Dump(world, result.Start, result.Target));
        Console.WriteLine($"seed {result.SeedUsed} (asked {seed}), map {world.Width}x{world.Height}");
        Console.WriteLine($"nodes {world.Nodes().Count()} / {world.Width * world.Height}, start→target {result.Start.DistanceTo(result.Target)} hexes");
        Console.WriteLine("legend: . empty  h home  w workstation  S server  R router  i iot  C controller  * start  $ target");
        foreach (Link link in world.Links)
        {
            Console.WriteLine($"link {link.Kind}: {link.A.ToOffset()} - {link.B.ToOffset()}");
        }
    }
}
