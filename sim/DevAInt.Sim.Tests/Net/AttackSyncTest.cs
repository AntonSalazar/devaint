using System.Collections.Generic;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты удара по сети: просьба клиента, удар на сервере, сообщение всем, кто видит атакующего.</summary>
public class AttackSyncTest
{
    /// <summary>Клиент просит удар, сервер бьёт сущность впереди, оба клиента узнают об ударе.</summary>
    [Fact]
    public void AttackReachesServerAndClients()
    {
        NetRun run = new();
        GameClient attacker = run.Connect();
        GameClient watcher = run.Connect();
        run.Tick(1, attacker, watcher);

        // Оба игрока появляются в одной точке, а удар достаёт и того, кто стоит вплотную
        run.Sim.Entity(watcher.Replica.OwnEntityId).Position = new WorldPos(3.5, 3.5, 0);
        long dummy = run.Sim.CreateEntity(new WorldPos(6.5, 7.3, 0), "core:human");
        List<(long, long)> seen = [];
        List<(long, long)> watched = [];
        attacker.Replica.Attacked += (from, to, _) => seen.Add((from, to));
        watcher.Replica.Attacked += (from, to, _) => watched.Add((from, to));

        run.Strike(attacker, new WorldPos(6.5, 7.3, 0));
        run.Tick(1, attacker, watcher);

        long own = attacker.Replica.OwnEntityId;
        Assert.Equal(75, run.Sim.Entity(dummy).Stats.Value(run.Sim.StatTable.StatIndex("core:health")));
        Assert.Equal([(own, dummy)], seen);
        Assert.Equal([(own, dummy)], watched);
    }

    /// <summary>Удар, который убил цель, приходит до её исчезновения.</summary>
    [Fact]
    public void KillingBlowComesBeforeRemoval()
    {
        NetRun run = new();
        GameClient attacker = run.Connect();
        run.Tick(1, attacker);
        long dummy = run.Sim.CreateEntity(new WorldPos(6.5, 7.3, 0), "core:human");
        run.Sim.Entity(dummy).Stats.SetValue(run.Sim.StatTable.StatIndex("core:health"), 10);
        run.Tick(1, attacker);
        bool knownAtAttack = false;
        attacker.Replica.Attacked += (from, to, _) => knownAtAttack = attacker.Replica.Entity(to) is not null;

        run.Strike(attacker, new WorldPos(6.5, 7.3, 0));
        run.Tick(1, attacker);

        Assert.True(knownAtAttack, "target is still known when the attack arrives");
        Assert.Null(attacker.Replica.Entity(dummy));
    }

    /// <summary>Пробел по сети толкает: цель цела, клиент получает толчок, а не удар.</summary>
    [Fact]
    public void ShoveReachesClient()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        long dummy = run.Sim.CreateEntity(new WorldPos(6.5, 7.3, 0), "core:human");
        run.Tick(1, client);
        List<AttackKind> kinds = [];
        client.Replica.Attacked += (_, _, kind) => kinds.Add(kind);

        client.SendControl(run.Sim.ControlTable.Index("core:shove"), true);
        run.Tick(1, client);

        Assert.Equal([AttackKind.Shove], kinds);
        Assert.Equal(100, run.Sim.Entity(dummy).Stats.Value(run.Sim.StatTable.StatIndex("core:health")));
    }

    /// <summary>Как в Project Zomboid: цель сбоку без прицела толчок не достаёт, с зажатым прицелом на неё - достаёт.</summary>
    [Fact]
    public void AimedShoveHitsSideTarget()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);

        // Персонаж смотрит на юг, цель к западу и чуть позади: ровно сбоку считается впереди
        long dummy = run.Sim.CreateEntity(new WorldPos(5.8, 6.4, 0), "core:human");
        run.Tick(1, client);
        List<long> shoved = [];
        client.Replica.Attacked += (_, target, kind) =>
        {
            if (kind == AttackKind.Shove)
            {
                shoved.Add(target);
            }
        };
        int shove = run.Sim.ControlTable.Index("core:shove");

        client.SendControl(shove, true);
        run.Tick(15, client);
        int unaimed = shoved.Count;
        client.SendControl(run.Sim.ControlTable.Index("core:aim"), true);
        client.SendInput(MoveInput.None, new WorldPos(5.8, 6.4, 0));
        client.SendControl(shove, true);
        run.Tick(1, client);

        Assert.Equal(0, unaimed);
        Assert.Equal([dummy], shoved);
    }
}
