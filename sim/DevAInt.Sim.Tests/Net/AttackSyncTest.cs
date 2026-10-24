using System.Collections.Generic;
using DevAInt.Sim.Grid;
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
        attacker.Replica.Attacked += (from, to) => seen.Add((from, to));
        watcher.Replica.Attacked += (from, to) => watched.Add((from, to));

        attacker.SendAttack();
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
        attacker.Replica.Attacked += (from, to) => knownAtAttack = attacker.Replica.Entity(to) is not null;

        attacker.SendAttack();
        run.Tick(1, attacker);

        Assert.True(knownAtAttack, "target is still known when the attack arrives");
        Assert.Null(attacker.Replica.Entity(dummy));
    }
}
