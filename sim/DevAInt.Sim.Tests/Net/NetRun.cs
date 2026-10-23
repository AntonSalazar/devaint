using System;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Сервер на loopback с картой и клиенты к нему.</summary>
public sealed class NetRun
{
    /// <summary>Транспорт в памяти.</summary>
    private readonly LoopbackTransport _transport = new();

    /// <summary>Сервер над sim с картой mapId; extraMod - мод с картой, если нужна не core.</summary>
    public NetRun(string mapId = "core:starter_room", string? extraMod = null, int radius = GameServer.DefaultSubscribeRadius)
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        string[] ids = ["core"];
        if (extraMod is not null)
        {
            mods.Manifest("test", depends: "core").Write("test/data/map.jsonc", extraMod);
            ids = ["core", "test"];
        }

        ModLoadResult result = mods.Load(ids);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Sim = new Simulation(result, Log, mapId);
        Server = new GameServer(Sim, _transport, Log, radius);
    }

    /// <summary>Лог сервера.</summary>
    public ListLog Log { get; } = new();

    /// <summary>Sim сервера.</summary>
    public Simulation Sim { get; }

    /// <summary>Сервер.</summary>
    public GameServer Server { get; }

    /// <summary>Новый клиент; сервер увидит его на следующем тике.</summary>
    public GameClient Connect() => new(_transport.Connect(), Sim.StatTable);

    /// <summary>Голое подключение без клиента, чтобы слать серверу что угодно.</summary>
    public IConnection ConnectRaw() => _transport.Connect();

    /// <summary>Тики сервера, после каждого клиенты применяют пакеты.</summary>
    public void Tick(int count, params GameClient[] clients)
    {
        for (int i = 0; i < count; i++)
        {
            Server.Tick();
            foreach (GameClient client in clients)
            {
                client.Update();
            }
        }
    }

    /// <summary>Проверка, что реплика совпадает с сервером: сущности, мудлы, свои статы и мир в её чанках.</summary>
    public void AssertReplicaMatches(GameClient client)
    {
        Replica replica = client.Replica;
        Assert.Equal(Sim.Clock.Tick, replica.Tick);
        foreach (ReplicaEntity seen in replica.Entities)
        {
            Entity entity = Sim.Entity(seen.Id);
            Assert.Equal(entity.Position.X, seen.X, 4);
            Assert.Equal(entity.Position.Y, seen.Y, 4);
            Assert.Equal(entity.Position.Z, seen.Z);
            for (int i = 0; i < Sim.StatTable.MoodleCount; i++)
            {
                Assert.Equal(entity.Stats.MoodleLevel(i), seen.MoodleLevels[i]);
            }
        }

        Entity own = Sim.Entity(replica.OwnEntityId);
        for (int i = 0; i < Sim.StatTable.StatCount; i++)
        {
            Assert.Equal((float)own.Stats.Value(i), replica.OwnStats[i]);
        }

        World world = replica.World!;
        foreach ((int chunkX, int chunkY) in replica.Chunks)
        {
            AssertChunkMatches(world, chunkX, chunkY);
        }
    }

    /// <summary>Полы и рёбра чанка у клиента такие же, как на сервере.</summary>
    private void AssertChunkMatches(World world, int chunkX, int chunkY)
    {
        for (int z = World.MinLevel; z <= World.MaxLevel; z++)
        {
            for (int y = chunkY * World.ChunkSize; y < Math.Min((chunkY + 1) * World.ChunkSize, world.Height + 1); y++)
            {
                for (int x = chunkX * World.ChunkSize; x < Math.Min((chunkX + 1) * World.ChunkSize, world.Width + 1); x++)
                {
                    CellPos cell = new(x, y, z);
                    Assert.Equal(Sim.World.Floor(cell), world.Floor(cell));
                    Assert.Equal(Sim.World.GetOwnedEdge(cell, true), world.GetOwnedEdge(cell, true));
                    Assert.Equal(Sim.World.GetOwnedEdge(cell, false), world.GetOwnedEdge(cell, false));
                }
            }
        }
    }
}
