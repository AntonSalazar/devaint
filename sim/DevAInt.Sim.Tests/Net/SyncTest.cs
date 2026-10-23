using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты синхронизации сервера и реплики через loopback.</summary>
public class SyncTest
{
    /// <summary>Длинная полоса пола 300 на 8 клеток для проверки подписки на чанки.</summary>
    private const string StripMap = """
        { "maps": [ { "id": "test:strip", "width": 300, "height": 8, "spawn": { "x": 5, "y": 4 },
          "build": [ { "floor": "core:wood_floor", "x": 0, "y": 0, "w": 300, "h": 8 } ] } ] }
        """;

    /// <summary>После подключения клиент знает свою сущность, комнату и стоит в точке появления.</summary>
    [Fact]
    public void ConnectGivesWelcomeAndRoom()
    {
        NetRun run = new();
        GameClient client = run.Connect();

        run.Tick(1, client);

        Replica replica = client.Replica;
        Assert.Equal(run.Server.ClientEntity(0), replica.OwnEntityId);
        ReplicaEntity own = replica.Entity(replica.OwnEntityId)!;
        Assert.Equal((6.5f, 6.5f), (own.X, own.Y));
        Assert.False(replica.World!.GetEdge(new CellPos(6, 8, 0), Side.South).IsPassable);
        run.AssertReplicaMatches(client);
    }

    /// <summary>Итог этапа 6: после тиков с движением, статами и мудлами реплика совпадает с сервером.</summary>
    [Fact]
    public void ReplicaMatchesServerAfterTicks()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        long npc = run.Sim.CreateEntity(new WorldPos(3.5, 3.5, 0));
        run.Sim.Entity(npc).Input = new MoveInput(1, 0.2);
        run.Tick(1, client);

        int hunger = run.Sim.StatTable.StatIndex("core:hunger");
        for (int i = 0; i < 100; i++)
        {
            client.SendInput(new MoveInput(i % 30 < 15 ? 1 : -1, i % 20 < 10 ? 0.5 : -0.5));
            if (i == 40)
            {
                run.Sim.Entity(client.Replica.OwnEntityId).Stats.SetValue(hunger, 75);
                run.Sim.Entity(npc).Stats.SetValue(hunger, 95);
            }

            run.Tick(1, client);
            run.AssertReplicaMatches(client);
        }

        Assert.Equal(2, client.Replica.Entities.Count);
        Assert.Equal(2, client.Replica.Entity(client.Replica.OwnEntityId)!.MoodleLevels[run.Sim.StatTable.MoodleIndex("core:hungry")]);
    }

    /// <summary>Дверь, открытая на сервере, приходит клиенту изменением ребра.</summary>
    [Fact]
    public void OpenedDoorReachesClient()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        List<(CellPos, bool)> changed = [];
        client.Replica.EdgeChanged += (owner, north) => changed.Add((owner, north));
        CellPos doorCell = new(6, 8, 0);

        run.Sim.World.SetEdge(doorCell, Side.South, run.Sim.World.GetEdge(doorCell, Side.South) with { Open = true });
        run.Tick(1, client);

        Assert.Equal([(new CellPos(6, 9, 0), true)], changed);
        Assert.True(client.Replica.World!.GetEdge(doorCell, Side.South).Open);
        run.Tick(1, client);
        Assert.Single(changed);
    }

    /// <summary>Уход далеко: дальние чанки выгружаются, новые приходят, чужая сущность появляется и исчезает.</summary>
    [Fact]
    public void FarChunksUnloadAndLoad()
    {
        NetRun run = new("test:strip", StripMap, radius: 1);
        GameClient client = run.Connect();
        long npc = run.Sim.CreateEntity(new WorldPos(200.5, 4.5, 0));
        run.Tick(1, client);
        Assert.Equal([(0, 0), (1, 0)], client.Replica.Chunks.Order());
        Assert.Null(client.Replica.Entity(npc));

        // Перенос своей сущности к чужой: чанк 6 из 300 / 32
        Entity own = run.Sim.Entity(client.Replica.OwnEntityId);
        own.Position = new WorldPos(199.5, 4.5, 0);
        run.Tick(1, client);
        Assert.Equal([(5, 0), (6, 0), (7, 0)], client.Replica.Chunks.Order());
        Assert.NotNull(client.Replica.Entity(npc));
        run.AssertReplicaMatches(client);

        own.Position = new WorldPos(5.5, 4.5, 0);
        run.Tick(1, client);
        Assert.Null(client.Replica.Entity(npc));
        Assert.Equal([(0, 0), (1, 0)], client.Replica.Chunks.Order());
        run.AssertReplicaMatches(client);
    }

    /// <summary>Сущность, ушедшая в чанк вне зоны, исчезает, вернувшись - появляется снова.</summary>
    [Fact]
    public void EntityLeavingZoneIsRemovedAndReturns()
    {
        NetRun run = new("test:strip", StripMap, radius: 1);
        GameClient client = run.Connect();
        long npc = run.Sim.CreateEntity(new WorldPos(10.5, 4.5, 0));
        run.Tick(1, client);
        List<string> events = [];
        client.Replica.EntityRemoved += entity => events.Add($"removed {entity.Id}");
        client.Replica.EntitySpawned += entity => events.Add($"spawned {entity.Id}");

        run.Sim.Entity(npc).Position = new WorldPos(150.5, 4.5, 0);
        run.Tick(1, client);
        run.Sim.Entity(npc).Position = new WorldPos(20.5, 4.5, 0);
        run.Tick(1, client);

        Assert.Equal([$"removed {npc}", $"spawned {npc}"], events);
        run.AssertReplicaMatches(client);
    }

    /// <summary>События первого пакета идут в порядке: мир, чанк, сущность, статы.</summary>
    [Fact]
    public void FirstFrameEventsInOrder()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        List<string> events = [];
        client.Replica.WorldCreated += () => events.Add("world");
        client.Replica.ChunkLoaded += (x, y) => events.Add($"chunk {x},{y}");
        client.Replica.EntitySpawned += entity => events.Add("entity");
        client.Replica.OwnStatsChanged += () => events.Add("stats");

        run.Tick(1, client);

        Assert.Equal(["world", "chunk 0,0", "chunk 0,1", "chunk 1,0", "chunk 1,1", "entity", "stats"], events);
    }

    /// <summary>Отключение удаляет сущность игрока, другой клиент видит, что она исчезла.</summary>
    [Fact]
    public void DisconnectRemovesEntity()
    {
        NetRun run = new();
        GameClient first = run.Connect();
        GameClient second = run.Connect();
        run.Tick(1, first, second);
        Assert.Equal(2, second.Replica.Entities.Count);
        long firstEntity = first.Replica.OwnEntityId;

        run.Server.ClientConnection(0).Disconnect();
        run.Tick(1, first, second);

        Assert.Equal(1, run.Server.ClientCount);
        Assert.Null(second.Replica.Entity(firstEntity));
    }

    /// <summary>Мусорный пакет от клиента разрывает подключение с записью в лог.</summary>
    [Fact]
    public void BadInputDisconnects()
    {
        NetRun run = new();
        IConnection raw = run.ConnectRaw();
        run.Tick(1);

        raw.Send([0xFF]);
        run.Tick(1);

        Assert.False(raw.IsConnected);
        Assert.Contains("bad input packet", Assert.Single(run.Log.Errors));
        Assert.Equal(0, run.Server.ClientCount);
    }

    /// <summary>200 движущихся сущностей в зоне: размер пакета за тик укладывается в оценку трафика концепта.</summary>
    [Fact]
    public void PacketFor200MoversIsSmall()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        for (int i = 0; i < 200; i++)
        {
            long id = run.Sim.CreateEntity(new WorldPos(2.5 + (i % 10), 2.5 + (i / 10 % 2), 0));
            run.Sim.Entity(id).Input = new MoveInput(System.Math.Cos(i), System.Math.Sin(i));
        }

        run.Tick(5, client);
        int bytes = run.Server.LastFrameBytes(0);

        // Около 12 байт на движение: тип, id, два float и этаж
        Assert.InRange(bytes, 1, 200 * 14);
    }
}
