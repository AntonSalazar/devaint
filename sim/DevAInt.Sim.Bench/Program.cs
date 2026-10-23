using System;
using System.Diagnostics;
using System.IO;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Lua;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Bench;

/// <summary>Замеры тика sim на 1000 сущностей: статы, хук tick, движение со стенами, 1000 вызовов хука за тик.</summary>
internal static class Program
{
    /// <summary>Число сущностей.</summary>
    private const int Entities = 1000;

    /// <summary>Тиков прогрева перед замером.</summary>
    private const int WarmupTicks = 3000;

    /// <summary>Тиков в замере.</summary>
    private const int MeasuredTicks = 2000;

    /// <summary>Запуск: аргумент - путь к папке mods репозитория.</summary>
    private static int Main(string[] args)
    {
        string repoMods = args.Length > 0 ? args[0] : "mods";
        string temp = Path.Combine(Path.GetTempPath(), "devaint-bench-" + Guid.NewGuid().ToString("N"), "mods");
        try
        {
            CopyDirectory(Path.Combine(repoMods, "core"), Path.Combine(temp, "core"));
            Directory.CreateDirectory(Path.Combine(temp, "bench", "scripts"));
            File.WriteAllText(Path.Combine(temp, "bench", "mod.jsonc"), """{ "id": "bench", "name": "Bench", "version": "0.1", "api": 1, "depends": ["core"] }""");

            Console.WriteLine($"{Entities} entities, {MeasuredTicks} ticks, budget per tick {1000.0 / GameClock.TicksPerSecond:F0} ms");
            MeasureSimulation(temp, "stats only, no Lua", string.Empty);
            MeasureSimulation(temp, "tick hook reads 1 stat of every entity", $$"""
                hooks.add("tick", function()
                  local sum = 0
                  for id = 1, {{Entities}} do sum = sum + stats.get(id, "core:hunger") end
                end)
                """);
            MeasureWalking(temp);
            MeasureServer(temp);
            MeasureHookCalls(temp);
            return 0;
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(temp)!, recursive: true);
        }
    }

    /// <summary>Тик Simulation с модом bench из одного скрипта.</summary>
    private static void MeasureSimulation(string mods, string title, string script)
    {
        File.WriteAllText(Path.Combine(mods, "bench", "scripts", "main.lua"), script);
        Simulation sim = new(Load(mods), new ConsoleLog());
        for (int i = 0; i < Entities; i++)
        {
            sim.CreateEntity();
        }

        Measure(title, sim.Tick);
    }

    /// <summary>Все сущности идут по двору стартовой комнаты и упираются в стены.</summary>
    private static void MeasureWalking(string mods)
    {
        File.WriteAllText(Path.Combine(mods, "bench", "scripts", "main.lua"), string.Empty);
        Simulation sim = new(Load(mods), new ConsoleLog(), "core:starter_room");
        for (int i = 0; i < Entities; i++)
        {
            // Разные стартовые точки и направления по двору, чтобы часть упиралась в стены
            WorldPos start = new(2.5 + (i % 10), 2.5 + (i / 10 % 2), 0);
            long id = sim.CreateEntity(start);
            sim.Entity(id).Input = new MoveInput(Math.Cos(i), Math.Sin(i));
        }

        Measure("all entities walk with wall collisions", sim.Tick);
    }

    /// <summary>Сервер с одним клиентом, который видит 1000 идущих сущностей: тик sim и сборка пакета.</summary>
    private static void MeasureServer(string mods)
    {
        File.WriteAllText(Path.Combine(mods, "bench", "scripts", "main.lua"), string.Empty);
        Simulation sim = new(Load(mods), new ConsoleLog(), "core:starter_room");
        for (int i = 0; i < Entities; i++)
        {
            long id = sim.CreateEntity(new WorldPos(2.5 + (i % 10), 2.5 + (i / 10 % 2), 0));
            sim.Entity(id).Input = new MoveInput(Math.Cos(i), Math.Sin(i));
        }

        LoopbackTransport transport = new();
        GameServer server = new(sim, transport, new ConsoleLog());
        IConnection client = transport.Connect();
        int ticks = 0;
        Measure("server tick, 1 client sees all entities", () =>
        {
            // Разворот каждые 3 секунды, чтобы сущности не стояли у стен и двигались всегда
            if (++ticks % 30 == 0)
            {
                foreach (Entity entity in sim.Entities)
                {
                    entity.Input = new MoveInput(-entity.Input.X, -entity.Input.Y);
                }
            }

            server.Tick();
            while (client.TryReceive(out _))
            {
            }
        });
        Console.WriteLine($"  {"last packet",-45} {server.LastFrameBytes(0),9} bytes");
    }

    /// <summary>Худший случай: хук stat_threshold вызывается для каждой сущности каждый тик.</summary>
    private static void MeasureHookCalls(string mods)
    {
        File.WriteAllText(Path.Combine(mods, "bench", "scripts", "main.lua"), """
            hooks.add("stat_threshold", function(entity, moodle, old_level, new_level)
              if stats.get(entity, "core:hunger") > 1000 then print("never") end
            end)
            """);
        ModLoadResult loaded = Load(mods);
        StatTable table = new(loaded.Defs);
        StatSet[] stats = new StatSet[Entities + 1];
        for (int i = 1; i <= Entities; i++)
        {
            stats[i] = new StatSet(table);
        }

        LuaHost lua = new(table, id => stats[id], new ConsoleLog());
        lua.Load(loaded.Mods);
        MoodleChange change = new(table.MoodleIndex("core:hungry"), 0, 1);
        Measure($"{Entities} stat_threshold hook calls", () =>
        {
            for (int i = 1; i <= Entities; i++)
            {
                lua.RaiseStatThreshold(i, change);
            }
        });
    }

    /// <summary>Время и выделенная память на тик после прогрева.</summary>
    private static void Measure(string title, Action tick)
    {
        for (int i = 0; i < WarmupTicks; i++)
        {
            tick();
        }

        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch watch = Stopwatch.StartNew();
        for (int i = 0; i < MeasuredTicks; i++)
        {
            tick();
        }

        watch.Stop();
        double microseconds = watch.Elapsed.TotalMilliseconds * 1000 / MeasuredTicks;
        double kilobytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0 / MeasuredTicks;
        double budget = microseconds / (10_000.0 / GameClock.TicksPerSecond);
        Console.WriteLine($"  {title,-45} {microseconds,9:F1} us/tick  {budget,6:F2}% of budget  {kilobytes,8:F1} KB/tick");
    }

    /// <summary>Загрузка core и bench, ошибки - исключение.</summary>
    private static ModLoadResult Load(string mods)
    {
        ModLoadResult result = ModLoader.Load(mods, ["core", "bench"]);
        return result.Ok ? result : throw new InvalidOperationException(string.Join("\n", result.Errors));
    }

    /// <summary>Копия папки со всеми файлами.</summary>
    private static void CopyDirectory(string source, string target)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string path = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(file, path);
        }
    }

    /// <summary>Лог в консоль.</summary>
    private sealed class ConsoleLog : ISimLog
    {
        /// <inheritdoc/>
        public void Info(string message) => Console.WriteLine(message);

        /// <inheritdoc/>
        public void Error(string message) => Console.Error.WriteLine(message);
    }
}
