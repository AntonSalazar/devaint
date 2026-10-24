using DevAInt.Sim.Grid;
using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты смерти по сети: вид существа у клиента, новый персонаж игрока после смерти.</summary>
public class DeathSyncTest
{
    /// <summary>Клиент знает вид своей сущности.</summary>
    [Fact]
    public void ClientKnowsCreature()
    {
        NetRun run = new();
        GameClient client = run.Connect();

        run.Tick(1, client);

        ReplicaEntity own = client.Replica.Entity(client.Replica.OwnEntityId)!;
        Assert.Equal(run.Sim.CreatureTable.Index("core:human"), own.Creature);
    }

    /// <summary>Умерший игрок получает нового персонажа в точке появления, клиент переключается на него.</summary>
    [Fact]
    public void DeadPlayerRespawns()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        GameClient other = run.Connect();
        run.Tick(1, client, other);
        long dead = client.Replica.OwnEntityId;
        Entity body = run.Sim.Entity(dead);
        body.Position = new WorldPos(3.5, 3.5, 0);
        run.Tick(1, client, other);

        body.Stats.SetValue(run.Sim.StatTable.StatIndex("core:health"), 0);
        run.Tick(1, client, other);

        long alive = client.Replica.OwnEntityId;
        Assert.NotEqual(dead, alive);
        Assert.Null(client.Replica.Entity(dead));
        Assert.Null(other.Replica.Entity(dead));
        Assert.Equal(new WorldPos(6.5, 6.5, 0), run.Sim.Entity(alive).Position);
        Assert.Equal(new WorldPos(6.5, 6.5, 0), client.OwnPosition);
        Assert.Equal(100f, client.Replica.OwnStats[run.Sim.StatTable.StatIndex("core:health")]);
        Assert.Contains("player player1 died", run.Log.Infos);
    }
}
