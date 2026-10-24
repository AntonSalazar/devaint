using System.Collections.Generic;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты синхронизации инвентаря и действий через loopback.</summary>
public class ActionSyncTest
{
    /// <summary>После подключения клиент знает свои три яблока.</summary>
    [Fact]
    public void ClientGetsInventory()
    {
        NetRun run = new();
        GameClient client = run.Connect();

        run.Tick(1, client);

        Assert.Equal(3, client.Replica.Inventory.Count);
        Assert.All(client.Replica.Inventory, item => Assert.Equal("core:apple", run.Sim.ItemTable.Item(item.Item).Id));
        run.AssertReplicaMatches(client);
    }

    /// <summary>Клиент просит съесть яблоко: прогресс растёт по тикам, по окончании яблоко пропадает из инвентаря.</summary>
    [Fact]
    public void EatingThroughNetwork()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        List<string> events = [];
        client.Replica.ActionStarted += entity => events.Add("started");
        client.Replica.ActionEnded += (entity, completed) => events.Add(completed ? "completed" : "interrupted");
        client.Replica.InventoryChanged += () => events.Add("inventory");
        int eat = run.Sim.ItemTable.ActionIndex("core:eat");

        client.SendStartAction(client.Replica.Inventory[0].Id, eat);
        run.Tick(1, client);
        ReplicaEntity own = client.Replica.Entity(client.Replica.OwnEntityId)!;
        Assert.NotNull(own.Action);
        float early = own.Action!.Progress(client.Replica.Tick);

        run.Tick(20, client);
        Assert.True(own.Action!.Progress(client.Replica.Tick) > early, "progress grows with ticks");
        run.AssertReplicaMatches(client);

        run.Tick(20, client);
        Assert.Null(own.Action);
        Assert.Equal(2, client.Replica.Inventory.Count);
        Assert.Equal(["started", "completed", "inventory"], events);
        run.AssertReplicaMatches(client);
    }

    /// <summary>Движение клиента прерывает действие, другой клиент видит начало и прерывание.</summary>
    [Fact]
    public void OtherClientSeesInterruptedAction()
    {
        NetRun run = new();
        GameClient eater = run.Connect();
        GameClient watcher = run.Connect();
        run.Tick(1, eater, watcher);
        long eaterId = eater.Replica.OwnEntityId;
        List<string> seen = [];
        watcher.Replica.ActionStarted += entity => seen.Add($"started {entity.Id}");
        watcher.Replica.ActionEnded += (entity, completed) => seen.Add($"{(completed ? "completed" : "interrupted")} {entity.Id}");

        eater.SendStartAction(eater.Replica.Inventory[0].Id, run.Sim.ItemTable.ActionIndex("core:eat"));
        run.Tick(5, eater, watcher);
        eater.SendInput(new MoveInput(1, 0));
        run.Tick(1, eater, watcher);

        Assert.Equal([$"started {eaterId}", $"interrupted {eaterId}"], seen);
        Assert.Equal(3, eater.Replica.Inventory.Count);
        run.AssertReplicaMatches(watcher);
    }

    /// <summary>Отмена с клиента прерывает действие.</summary>
    [Fact]
    public void ClientCancelsAction()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);

        client.SendStartAction(client.Replica.Inventory[0].Id, run.Sim.ItemTable.ActionIndex("core:eat"));
        run.Tick(3, client);
        client.SendCancelAction();
        run.Tick(1, client);

        Assert.Null(client.Replica.Entity(client.Replica.OwnEntityId)!.Action);
        Assert.Equal(3, client.Replica.Inventory.Count);
    }

    /// <summary>Номер действия вне таблицы - мусорный пакет, подключение разрывается.</summary>
    [Fact]
    public void BadActionIndexDisconnects()
    {
        NetRun run = new();
        IConnection raw = run.ConnectRaw();
        raw.Send(Protocol.Write(new HelloMessage("anna")));
        run.Tick(1);

        raw.Send(Protocol.Write(new StartActionMessage(1, 99)));
        run.Tick(1);

        Assert.False(raw.IsConnected);
        Assert.Contains("action 99 is out of range", Assert.Single(run.Log.Errors));
    }
}
