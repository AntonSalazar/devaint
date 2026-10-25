using System.Linq;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Creatures;

/// <summary>Тесты толчка: отлёт со стенами, оглушение, перезарядка.</summary>
public class ShoveTest
{
    /// <summary>Моды core и zombies.</summary>
    private static readonly ModLoadResult _mods = ModLoader.Load(RepoMods.Root, ["core", "zombies"]);

    /// <summary>Толчок отбрасывает зомби на 1.2 м от человека, урона нет, событие - толчок.</summary>
    [Fact]
    public void ShovePushesWithoutDamage()
    {
        (Simulation sim, Entity player, Entity zombie) = Scene(new WorldPos(3.5, 5.5, 0), new WorldPos(3.5, 6.3, 0));

        sim.Control(player.Id, sim.ControlTable.Index("core:shove"), true);
        sim.Tick();
        (long, long, AttackKind) shove = sim.Attacks.Single();

        // В тике толчка зомби успел шагнуть к человеку, отлёт считается от места толчка
        double shovedAt = zombie.Position.Y;
        Tick(sim, 3);

        Assert.Equal((player.Id, zombie.Id, AttackKind.Shove), shove);
        Assert.Equal(shovedAt + 1.2, zombie.Position.Y, 6);
        Assert.Equal(100, Health(sim, zombie));
    }

    /// <summary>Оглушённый зомби стоит и не кусает, после оглушения снова идёт к цели.</summary>
    [Fact]
    public void StunnedZombieWaits()
    {
        (Simulation sim, Entity player, Entity zombie) = Scene(new WorldPos(3.5, 5.5, 0), new WorldPos(3.5, 6.2, 0));
        zombie.StunnedUntilTick = sim.Clock.Tick + 10;

        Tick(sim, 9);
        WorldPos stunned = zombie.Position;
        double health = Health(sim, player);
        Tick(sim, 5);

        Assert.Equal(new WorldPos(3.5, 6.2, 0), stunned);
        Assert.Equal(100, health);
        Assert.True(Health(sim, player) < 100, "the zombie bites after the stun");
    }

    /// <summary>Стена останавливает отлёт: зомби у стены комнаты не пролетает сквозь неё.</summary>
    [Fact]
    public void WallStopsKnock()
    {
        // Человек внутри комнаты у западной стены смотрит на запад, зомби между ним и стеной
        (Simulation sim, Entity player, Entity zombie) = Scene(new WorldPos(5.3, 6.5, 0), new WorldPos(4.5, 6.5, 0));
        player.Facing = (-1, 0);

        sim.Control(player.Id, sim.ControlTable.Index("core:shove"), true);
        Tick(sim, 4);

        Assert.True(zombie.Position.X >= 4.3 - 1e-6, $"zombie stays inside the room at {zombie.Position.X}");
    }

    /// <summary>Второй толчок до конца перезарядки не проходит.</summary>
    [Fact]
    public void ShoveHasCooldown()
    {
        (Simulation sim, Entity player, Entity zombie) = Scene(new WorldPos(3.5, 5.5, 0), new WorldPos(3.5, 6.3, 0));
        int shove = sim.ControlTable.Index("core:shove");

        sim.Control(player.Id, shove, true);
        sim.Tick();
        zombie.Position = new WorldPos(3.5, 6.3, 0);
        sim.Control(player.Id, shove, true);
        sim.Tick();

        Assert.Empty(sim.Attacks);
    }

    /// <summary>Мир стартовой комнаты без зомби мода: человек и один зомби в заданных точках, человек смотрит на юг.</summary>
    private static (Simulation Sim, Entity Player, Entity Zombie) Scene(WorldPos player, WorldPos zombie)
    {
        Assert.True(_mods.Ok, string.Join("\n", _mods.Errors));
        Simulation sim = new(_mods, new ListLog(), "core:starter_room");
        foreach (Entity entity in sim.Entities.ToList())
        {
            sim.RemoveEntity(entity.Id);
        }

        Entity human = sim.Entity(sim.CreateEntity(player, "core:human"));
        human.Facing = (0, 1);
        return (sim, human, sim.Entity(sim.CreateEntity(zombie, "zombies:zombie")));
    }

    /// <summary>Здоровье сущности.</summary>
    private static double Health(Simulation sim, Entity entity) => entity.Stats.Value(sim.StatTable.StatIndex("core:health"));

    /// <summary>Тики sim.</summary>
    private static void Tick(Simulation sim, int count)
    {
        for (int i = 0; i < count; i++)
        {
            sim.Tick();
        }
    }
}
