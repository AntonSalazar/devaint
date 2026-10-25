using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Lua;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Saves;
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
            CopyDirectory(Path.Combine(repoMods, "zombies"), Path.Combine(temp, "zombies"));
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
            MeasureSave(temp, "core:starter_room", entities: 0);
            WriteHugeMap(temp);
            MeasureSave(temp, "bench:huge", Entities);
            MeasureZombies(temp);
            MeasureVision(temp);
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

        LuaHost lua = new(table, id => stats[id], () => [], new ConsoleLog());
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

    /// <summary>Карта 4096 на 4096 клеток: пол везде и комнаты 10 на 10 с дверью через каждые 32 клетки.</summary>
    private static void WriteHugeMap(string mods)
    {
        StringBuilder build = new("""{ "floor": "core:wood_floor", "x": 0, "y": 0, "w": 4096, "h": 4096 }""");
        for (int y = 0; y < 4096; y += 32)
        {
            for (int x = 0; x < 4096; x += 32)
            {
                build.Append($$""", { "walls": "core:wood_wall", "x": {{x + 4}}, "y": {{y + 4}}, "w": 10, "h": 10 }""");
                build.Append($$""", { "door": "core:wood_door", "x": {{x + 8}}, "y": {{y + 13}}, "side": "south" }""");
            }
        }

        string map = $$"""{ "maps": [ { "id": "bench:huge", "width": 4096, "height": 4096, "spawn": { "points": [ { "x": 1, "y": 1 } ] }, "build": [ {{build}} ] } ] }""";
        Directory.CreateDirectory(Path.Combine(mods, "bench", "data"));
        File.WriteAllText(Path.Combine(mods, "bench", "data", "maps.jsonc"), map);
    }

    /// <summary>Сохранение и загрузка карты с сущностями: полная запись, запись после одной правки, загрузка, размер.</summary>
    private static void MeasureSave(string mods, string mapId, int entities)
    {
        File.WriteAllText(Path.Combine(mods, "bench", "scripts", "main.lua"), string.Empty);
        ModLoadResult loaded = Load(mods);
        Simulation sim = new(loaded, new ConsoleLog(), mapId);
        Random random = new(1);
        for (int i = 0; i < entities; i++)
        {
            sim.CreateEntity(new WorldPos(random.Next(sim.World.Width) + 0.5, random.Next(sim.World.Height) + 0.5, 0));
        }

        string directory = Path.Combine(Path.GetDirectoryName(mods)!, "save-" + mapId.Replace(':', '-'));
        SaveGame save = new(directory);
        double full = Time(() => save.Save(sim));
        sim.World.SetFloor(new CellPos(1, 1, 0), 0);
        double one = Time(() => save.Save(sim));
        double load = Time(() => new SaveGame(directory).Load(loaded, new ConsoleLog()));

        string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        string[] regions = Directory.GetFiles(Path.Combine(directory, "regions"));
        double size = files.Sum(file => new FileInfo(file).Length) / 1024.0;
        double raw = regions.Sum(file => SaveFile.ReadBinary(file, "DVRG").Length) / 1024.0;
        Console.WriteLine($"  save {mapId}, {entities} entities: {regions.Length} regions, {size:F0} KB on disk, {raw:F0} KB before zlib");
        Console.WriteLine($"    full save {full:F0} ms, save after one change {one:F0} ms, load {load:F0} ms");
    }

    /// <summary>1000 зомби гонятся за 32 игроками, которые бегают по кругу среди комнат большой карты.</summary>
    private static void MeasureZombies(string mods)
    {
        ModLoadResult loaded = ModLoader.Load(mods, ["core", "zombies", "bench"]);
        Simulation sim = new(loaded.Ok ? loaded : throw new InvalidOperationException(string.Join("\n", loaded.Errors)), new ConsoleLog(), "bench:huge");
        Random random = new(1);
        List<Entity> players = [];
        for (int i = 0; i < 32; i++)
        {
            players.Add(sim.Entity(sim.CreateEntity(new WorldPos(2040.5 + random.Next(16), 2040.5 + random.Next(16), 0), "core:human")));
        }

        // Зомби вокруг игроков в квадрате 60 на 60 метров: почти все видят кого-то или идут на последнее место
        for (int i = 0; i < Entities; i++)
        {
            sim.CreateEntity(new WorldPos(2018.5 + random.Next(60), 2018.5 + random.Next(60), 0), "zombies:zombie");
        }

        int health = sim.StatTable.StatIndex("core:health");
        double time = 0;
        Measure($"{Entities} zombies chase 32 players", () =>
        {
            // Игроки бессмертны и бегают по кругу, чтобы зомби всё время искали путь заново
            time += 0.05;
            for (int i = 0; i < players.Count; i++)
            {
                players[i].Stats.SetValue(health, 100);
                players[i].Input = new MoveInput(Math.Cos(time + i), Math.Sin(time + i));
            }

            sim.Tick();
        });
    }

    /// <summary>Расчёт видимости человека из core среди комнат большой карты: одна видимость на кадр клиента.</summary>
    private static void MeasureVision(string mods)
    {
        ModLoadResult loaded = ModLoader.Load(mods, ["core", "zombies", "bench"]);
        Simulation sim = new(loaded.Ok ? loaded : throw new InvalidOperationException(string.Join("\n", loaded.Errors)), new ConsoleLog(), "bench:huge");
        VisionDef vision = sim.CreatureTable.Creature(sim.CreatureTable.Index("core:human")).Vision!;
        VisibilityMap map = new();
        double angle = 0;
        Measure($"vision {vision.Angle} deg, {vision.Range} m among rooms", () =>
        {
            // Взгляд поворачивается каждый раз, чтобы конус задевал разные стены
            angle += 0.1;
            Vision.Compute(sim.World, new WorldPos(2050.5, 2050.5, 0), (Math.Cos(angle), Math.Sin(angle)), vision, map);
        });
    }

    /// <summary>Время действия в миллисекундах.</summary>
    private static double Time(Action action)
    {
        Stopwatch watch = Stopwatch.StartNew();
        action();
        return watch.Elapsed.TotalMilliseconds;
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
