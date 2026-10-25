using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Creatures;

/// <summary>Тесты выбора точки появления: случайно без модов, хук choose_spawn, запрос entities.near, правило мода zombies.</summary>
public class SpawnTest
{
    /// <summary>Карта 10 на 10 с полом и тремя точками появления.</summary>
    private const string Map = """
        { "maps": [ { "id": "test:field", "width": 10, "height": 10,
          "spawn": { "points": [ { "x": 1, "y": 1 }, { "x": 5, "y": 5 }, { "x": 8, "y": 8 } ] },
          "build": [ { "floor": "core:wood_floor", "x": 0, "y": 0, "w": 10, "h": 10 } ] } ] }
        """;

    /// <summary>Лог.</summary>
    private readonly ListLog _log = new();

    /// <summary>Без хука точка случайная, но повторяемая: у двух миров одна и та же череда точек.</summary>
    [Fact]
    public void DefaultChoiceIsRepeatable()
    {
        using TempMods mods = Mods(null);

        WorldPos[] first = Spawn(NewSim(mods), 6);
        WorldPos[] second = Spawn(NewSim(mods), 6);

        Assert.Equal(first, second);
        Assert.True(first.Distinct().Count() > 1, "points are mixed");
    }

    /// <summary>Хук мода выбирает точку по номеру с единицы.</summary>
    [Fact]
    public void HookChoosesPoint()
    {
        using TempMods mods = Mods("""hooks.add("choose_spawn", function(player, points) return #points end)""");

        Assert.Equal(new WorldPos(8.5, 8.5, 0), Spawn(NewSim(mods), 1)[0]);
    }

    /// <summary>Поздний мод спрашивается первым; если он не ответил, отвечает ранний.</summary>
    [Fact]
    public void LaterModOverrides()
    {
        using TempMods mods = Mods("""hooks.add("choose_spawn", function() return 1 end)""");
        mods.Manifest("late", depends: "test").Write("late/scripts/main.lua", """
            hooks.add("choose_spawn", function(player) if player == "late" then return 2 end end)
            """);
        Simulation sim = new(Load(mods, "late"), _log, "test:field");

        Assert.Equal(new WorldPos(5.5, 5.5, 0), sim.Entity(sim.SpawnPlayer("late")).Position);
        Assert.Equal(new WorldPos(1.5, 1.5, 0), sim.Entity(sim.SpawnPlayer("other")).Position);
    }

    /// <summary>Неверный ответ хука - ошибка в лог и случайная точка.</summary>
    [Fact]
    public void BadAnswerIsLogged()
    {
        using TempMods mods = Mods("""hooks.add("choose_spawn", function() return 99 end)""");

        Spawn(NewSim(mods), 1);

        Assert.Equal("mod test: hook choose_spawn: answer 99 is not a point number 1..3", Assert.Single(_log.Errors));
    }

    /// <summary>entities.near видит сущности в радиусе на своём этаже, с видом и координатами.</summary>
    [Fact]
    public void EntitiesNearFindsNeighbours()
    {
        using TempMods mods = Mods("""
            hooks.add("choose_spawn", function()
              local list = entities.near(5.5, 5.5, 0, 2)
              if #list == 1 and list[1].creature == "core:human" and list[1].x == 6.5 then return 3 end
              return 1
            end)
            """);
        Simulation sim = NewSim(mods);
        sim.CreateEntity(new WorldPos(6.5, 5.5, 0), "core:human");
        sim.CreateEntity(new WorldPos(9.5, 9.5, 0), "core:human");

        Assert.Equal(new WorldPos(8.5, 8.5, 0), sim.Entity(sim.SpawnPlayer("anna")).Position);
    }

    /// <summary>Правило мода zombies: в комнате, пока там нет зомби; зомби в комнате - в первом безопасном углу двора.</summary>
    [Fact]
    public void ZombiesModAvoidsZombies()
    {
        ModLoadResult loaded = ModLoader.Load(RepoMods.Root, ["core", "zombies"]);
        Simulation sim = new(loaded, _log, "core:starter_room");
        Assert.Equal(4, sim.SpawnPoints.Count);

        WorldPos first = sim.Entity(sim.SpawnPlayer("anna")).Position;
        sim.CreateEntity(new WorldPos(6.5, 6.5, 0), "zombies:zombie");
        WorldPos second = sim.Entity(sim.SpawnPlayer("bob")).Position;

        Assert.Equal(new WorldPos(6.5, 6.5, 0), first);
        Assert.Equal(new WorldPos(2.5, 2.5, 0), second);
        Assert.Empty(_log.Errors);
    }

    /// <summary>Если у всех точек зомби рядом, правило берёт самую далёкую от зомби.</summary>
    [Fact]
    public void ZombiesModTakesFarthestWhenAllUnsafe()
    {
        ModLoadResult loaded = ModLoader.Load(RepoMods.Root, ["core", "zombies"]);
        Simulation sim = new(loaded, _log, "core:starter_room");
        sim.CreateEntity(new WorldPos(6.5, 6.5, 0), "zombies:zombie");
        sim.CreateEntity(new WorldPos(2.5, 3.5, 0), "zombies:zombie");
        sim.CreateEntity(new WorldPos(11.5, 3.5, 0), "zombies:zombie");
        sim.CreateEntity(new WorldPos(11.5, 8.5, 0), "zombies:zombie");

        WorldPos spawn = sim.Entity(sim.SpawnPlayer("anna")).Position;

        Assert.Equal(new WorldPos(11.5, 6.5, 0), spawn);
    }

    /// <summary>Позиции count новых игроков подряд.</summary>
    private static WorldPos[] Spawn(Simulation sim, int count) => [.. Enumerable.Range(0, count).Select(i => sim.Entity(sim.SpawnPlayer($"p{i}")).Position)];

    /// <summary>Моды core и test с картой и, если задан, скриптом.</summary>
    private static TempMods Mods(string? script)
    {
        TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("test", depends: "core").Write("test/data/map.jsonc", Map);
        if (script is not null)
        {
            mods.Write("test/scripts/main.lua", script);
        }

        return mods;
    }

    /// <summary>Загрузка модов core, test и extra.</summary>
    private static ModLoadResult Load(TempMods mods, string? extra = null)
    {
        ModLoadResult result = extra is null ? mods.Load("core", "test") : mods.Load("core", "test", extra);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return result;
    }

    /// <summary>Новый мир на поле.</summary>
    private Simulation NewSim(TempMods mods)
    {
        Simulation sim = new(Load(mods), _log, "test:field");
        Assert.Empty(sim.ScriptErrors);
        return sim;
    }
}
