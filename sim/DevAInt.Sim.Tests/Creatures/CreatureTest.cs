using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Creatures;

/// <summary>Тесты существ на модах core и zombies: погоня, укус, дверь, удар игрока, смерть и свой набор статов.</summary>
public class CreatureTest
{
    /// <summary>Моды репозитория core и zombies, грузятся один раз.</summary>
    private static readonly ModLoadResult _mods = LoadMods();

    /// <summary>Клетка двери стартовой комнаты.</summary>
    private static readonly CellPos _door = new(6, 8, 0);

    /// <summary>Лог.</summary>
    private readonly ListLog _log = new();

    /// <summary>Новый мир стартовой комнаты: три зомби из патча мода во дворе, дверь открыта.</summary>
    [Fact]
    public void ZombiesSpawnFromPatchedMap()
    {
        Simulation sim = NewSim();

        Assert.Equal(3, Zombies(sim).Count());
        Assert.True(sim.World.GetEdge(_door, Side.South).Open);
        Assert.Empty(_log.Errors);
    }

    /// <summary>Зомби у открытой двери видит игрока, входит в комнату и кусает.</summary>
    [Fact]
    public void ZombieChasesAndBites()
    {
        Simulation sim = NewSim();
        Entity player = sim.Entity(sim.SpawnPlayer("anna"));

        int ticks = TickUntil(sim, () => Health(sim, player) < 100, 400);

        Assert.True(ticks < 400, "the zombie bites within 40 seconds");
        Assert.Equal(90, Health(sim, player));
        Entity biter = Zombies(sim).OrderBy(zombie => Distance(zombie, player)).First();
        Assert.True(Distance(biter, player) <= 0.9, $"biter is next to the player: {Distance(biter, player)}");
    }

    /// <summary>Укусы идут с перезарядкой полторы секунды: за 3 секунды у двери не больше трёх укусов от одного зомби.</summary>
    [Fact]
    public void BitesHaveCooldown()
    {
        Simulation sim = NewSim();
        RemoveZombiesExceptNearest(sim);
        Entity player = sim.Entity(sim.SpawnPlayer("anna"));
        TickUntil(sim, () => Health(sim, player) < 100, 400);

        Tick(sim, 30);

        // Первый укус и ещё два за 30 тиков при перезарядке 15 тиков
        Assert.Equal(70, Health(sim, player));
    }

    /// <summary>Закрытая дверь останавливает зомби: игрок в комнате цел.</summary>
    [Fact]
    public void ClosedDoorKeepsZombiesOut()
    {
        Simulation sim = NewSim();
        sim.World.SetEdge(_door, Side.South, sim.World.GetEdge(_door, Side.South) with { Open = false });
        Entity player = sim.Entity(sim.SpawnPlayer("anna"));

        Tick(sim, 600);

        Assert.Equal(100, Health(sim, player));
        Assert.All(Zombies(sim), zombie => Assert.True(zombie.Position.Y > 9, $"zombie outside at {zombie.Position}"));
    }

    /// <summary>Сквозь стены зомби не видят: при закрытой двери они стоят на местах.</summary>
    [Fact]
    public void ZombiesDoNotSeeThroughWalls()
    {
        Simulation sim = NewSim();
        sim.World.SetEdge(_door, Side.South, sim.World.GetEdge(_door, Side.South) with { Open = false });
        WorldPos[] before = [.. Zombies(sim).Select(zombie => zombie.Position)];
        sim.SpawnPlayer("anna");

        Tick(sim, 100);

        Assert.Equal(before, Zombies(sim).Select(zombie => zombie.Position));
    }

    /// <summary>Игрок бьёт зомби впереди, четыре удара убивают его; удар чаще перезарядки не проходит.</summary>
    [Fact]
    public void PlayerKillsZombie()
    {
        Simulation sim = NewSim();
        RemoveZombiesExceptNearest(sim);
        Entity zombie = Zombies(sim).Single();
        Entity player = sim.Entity(sim.CreateEntity(zombie.Position with { Y = zombie.Position.Y - 0.8 }, "core:human"));
        player.Facing = (0, 1);

        sim.Attack(player.Id);
        sim.Attack(player.Id);
        Tick(sim, 1);
        sim.Attack(player.Id);
        Tick(sim, 1);
        Assert.Equal(75, Health(sim, zombie));

        for (int i = 0; i < 3; i++)
        {
            Tick(sim, 8);
            sim.Attack(player.Id);
            Tick(sim, 1);
        }

        Assert.DoesNotContain(zombie, sim.Entities);
        Assert.Contains(zombie, sim.Died);
    }

    /// <summary>Удар в спину не достаёт: цель позади взгляда.</summary>
    [Fact]
    public void AttackMissesBehind()
    {
        Simulation sim = NewSim();
        RemoveZombiesExceptNearest(sim);
        Entity zombie = Zombies(sim).Single();
        Entity player = sim.Entity(sim.CreateEntity(zombie.Position with { Y = zombie.Position.Y - 0.8 }, "core:human"));
        player.Facing = (0, -1);

        sim.Attack(player.Id);
        Tick(sim, 1);

        Assert.Equal(100, Health(sim, zombie));
    }

    /// <summary>Игрок с нулевым здоровьем умирает и уходит из мира.</summary>
    [Fact]
    public void PlayerDiesAtZeroHealth()
    {
        Simulation sim = NewSim();
        Entity player = sim.Entity(sim.SpawnPlayer("anna"));
        player.Stats.SetValue(sim.StatTable.StatIndex("core:health"), 5);

        TickUntil(sim, () => !sim.Entities.Contains(player), 400);

        Assert.Contains(player, sim.Died);
        Assert.Equal(0, Health(sim, player));
    }

    /// <summary>У зомби только здоровье: голод не растёт и мудлов нет; у человека растёт.</summary>
    [Fact]
    public void ZombieHasOnlyItsStats()
    {
        Simulation sim = NewSim();
        sim.World.SetEdge(_door, Side.South, sim.World.GetEdge(_door, Side.South) with { Open = false });
        Entity player = sim.Entity(sim.SpawnPlayer("anna"));
        Entity zombie = Zombies(sim).First();
        int hunger = sim.StatTable.StatIndex("core:hunger");
        zombie.Stats.SetValue(hunger, 95);

        Tick(sim, 1000);

        Assert.False(zombie.Stats.IsActive(hunger));
        Assert.Equal(0, zombie.Stats.Rate(hunger));
        Assert.Equal(0, zombie.Stats.MoodleLevel(sim.StatTable.MoodleIndex("core:hungry")));
        Assert.True(player.Stats.Value(hunger) > 0, "player gets hungry");
    }

    /// <summary>Тело из вида: зомби медленнее человека.</summary>
    [Fact]
    public void BodyComesFromCreature()
    {
        Simulation sim = NewSim();
        Entity zombie = Zombies(sim).First();
        Entity player = sim.Entity(sim.SpawnPlayer("anna"));

        Assert.Equal(0.9, zombie.Body.Speed);
        Assert.Equal(1.5, player.Body.Speed);
        Assert.Equal("core:human", player.Creature?.Id);
    }

    /// <summary>Существо карты на клетке без пола не появляется, ошибка в логе.</summary>
    [Fact]
    public void CreatureOffFloorIsLogged()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core").CopyRepoMod("zombies");
        mods.Manifest("test", depends: "zombies").Write("test/data/patch.jsonc", """
            { "patches": [ { "op": "add", "target": "core:starter_room", "path": "creatures", "value": { "creature": "zombies:zombie", "x": 20, "y": 20 } } ] }
            """);
        ModLoadResult result = mods.Load("core", "zombies", "test");
        Assert.True(result.Ok, string.Join("\n", result.Errors));

        Simulation sim = new(result, _log, "core:starter_room");

        Assert.Equal(3, Zombies(sim).Count());
        Assert.Equal("map core:starter_room: creature zombies:zombie at 20,20,0 is not on a floor", Assert.Single(_log.Errors));
    }

    /// <summary>Тики sim.</summary>
    private static void Tick(Simulation sim, int count)
    {
        for (int i = 0; i < count; i++)
        {
            sim.Tick();
        }
    }

    /// <summary>Тики до условия, но не больше limit; число сделанных тиков.</summary>
    private static int TickUntil(Simulation sim, System.Func<bool> done, int limit)
    {
        for (int i = 0; i < limit; i++)
        {
            sim.Tick();
            if (done())
            {
                return i + 1;
            }
        }

        return limit;
    }

    /// <summary>Здоровье сущности.</summary>
    private static double Health(Simulation sim, Entity entity) => entity.Stats.Value(sim.StatTable.StatIndex("core:health"));

    /// <summary>Зомби в мире.</summary>
    private static System.Collections.Generic.IEnumerable<Entity> Zombies(Simulation sim) => sim.Entities.Where(entity => entity.Creature?.Id == "zombies:zombie");

    /// <summary>Остаётся только зомби у двери, напротив проёма.</summary>
    private static void RemoveZombiesExceptNearest(Simulation sim)
    {
        foreach (Entity zombie in Zombies(sim).Where(zombie => zombie.Position.X != 6.5).ToList())
        {
            sim.RemoveEntity(zombie.Id);
        }
    }

    /// <summary>Расстояние между сущностями.</summary>
    private static double Distance(Entity a, Entity b) => System.Math.Sqrt(((a.Position.X - b.Position.X) * (a.Position.X - b.Position.X)) + ((a.Position.Y - b.Position.Y) * (a.Position.Y - b.Position.Y)));

    /// <summary>Загрузка core и zombies.</summary>
    private static ModLoadResult LoadMods()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core", "zombies"]);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return result;
    }

    /// <summary>Новый мир стартовой комнаты.</summary>
    private Simulation NewSim() => new(_mods, _log, "core:starter_room");
}
