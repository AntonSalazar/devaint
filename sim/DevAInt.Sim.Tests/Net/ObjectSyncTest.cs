using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты объектов мира по сети: снимок чанка, постановка и удаление, проверка данных сервера.</summary>
public class ObjectSyncTest
{
    /// <summary>Мебель стартовой комнаты приходит в снимке чанка: клетки у клиента заняты так же, как на сервере.</summary>
    [Fact]
    public void SnapshotCarriesObjects()
    {
        NetRun run = new();
        GameClient client = run.Connect();

        run.Tick(1, client);
        World world = client.Replica.World!;

        Assert.Equal(run.Sim.World.Objects.OrderBy(obj => obj.Id), world.Objects.OrderBy(obj => obj.Id));
        Assert.False(world.IsFree(new CellPos(5, 4, 0)), "table");
        Assert.True(world.BlocksSight(new CellPos(9, 4, 0)), "wardrobe");
    }

    /// <summary>Поставленный и убранный на сервере объект так же появляется и исчезает у клиента, с событием.</summary>
    [Fact]
    public void PlaceAndRemoveReachClient()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        List<long> changed = [];
        client.Replica.ObjectChanged += obj => changed.Add(obj.Id);
        ushort crate = run.Sim.WorldTable.ObjectIndex("core:crate");
        WorldObject obj = WorldObject.Create(run.Sim.World.NextObjectId, crate, run.Sim.WorldTable.Object(crate), new CellPos(3, 3, 0), 0);

        run.Sim.World.PlaceObject(obj);
        run.Tick(1, client);
        bool placed = !client.Replica.World!.IsFree(obj.Cell);
        run.Sim.World.RemoveObject(obj.Id);
        run.Tick(1, client);

        Assert.True(placed, "client sees the crate");
        Assert.True(client.Replica.World!.IsFree(obj.Cell), "client sees it removed");
        Assert.Equal([obj.Id, obj.Id], changed);
    }

    /// <summary>Неизвестный вид, объект не из своего чанка и повтор id от сервера - ProtocolException.</summary>
    [Fact]
    public void BadObjectsAreRejected()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        Replica replica = client.Replica;
        long table = run.Sim.World.Objects.First().Id;

        Assert.Throws<ProtocolException>(() => replica.Apply(Frame(new ObjectPlaceMessage(new ObjectData(100, 99, new CellPos(2, 2, 0), 0)))));
        Assert.Throws<ProtocolException>(() => replica.Apply(Frame(new ObjectPlaceMessage(new ObjectData(table, 1, new CellPos(2, 2, 0), 0)))));
        Assert.Throws<ProtocolException>(() => replica.Apply(Frame(new ObjectRemoveMessage(100))));
        Assert.Throws<ProtocolException>(() => replica.Apply(Frame(new ChunkSnapshotMessage(1, 0, [], [new ObjectData(101, 1, new CellPos(2, 2, 0), 0)]))));
    }

    /// <summary>Кадр сервера с одним сообщением.</summary>
    private static ServerFrame Frame(ServerMessage message) => new(1000, [message]);
}
