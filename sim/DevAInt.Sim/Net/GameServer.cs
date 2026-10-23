using System;
using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Motion;

namespace DevAInt.Sim.Net;

/// <summary>Сервер: подключения, ввод клиентов, тик sim и пакет изменений каждому клиенту.</summary>
/// <remarks>Сервер над sim и транспортом.</remarks>
public sealed class GameServer(Simulation sim, IServerTransport transport, ISimLog log, int subscribeRadius = GameServer.DefaultSubscribeRadius)
{
    /// <summary>Радиус подписки в чанках вокруг чанка своей сущности.</summary>
    public const int DefaultSubscribeRadius = 4;

    /// <summary>Sim, который сервер ведёт.</summary>
    private readonly Simulation _sim = sim;

    /// <summary>Источник новых подключений.</summary>
    private readonly IServerTransport _transport = transport;

    /// <summary>Лог ошибок клиентов.</summary>
    private readonly ISimLog _log = log;

    /// <summary>Радиус подписки в чанках.</summary>
    private readonly int _radius = subscribeRadius;

    /// <summary>Подключённые клиенты.</summary>
    private readonly List<ClientSession> _clients = [];

    /// <summary>Число подключённых клиентов.</summary>
    public int ClientCount => _clients.Count;

    /// <summary>Сущность клиента по номеру подключения, для тестов и отладки.</summary>
    public long ClientEntity(int index) => _clients[index].EntityId;

    /// <summary>Тик сервера: новые клиенты, их ввод, тик sim, пакеты клиентам.</summary>
    public void Tick()
    {
        while (_transport.TryAccept(out IConnection connection))
        {
            _clients.Add(new ClientSession(connection, _sim.CreateEntity(_sim.Spawn)));
        }

        foreach (ClientSession client in _clients)
        {
            ReceiveInputs(client);
        }

        _sim.Tick();

        foreach (ClientSession client in _clients.Where(client => !client.Connection.IsConnected).ToList())
        {
            _sim.RemoveEntity(client.EntityId);
            _clients.Remove(client);
        }

        foreach (ClientSession client in _clients)
        {
            byte[] packet = Protocol.Write(BuildFrame(client));
            client.LastFrameBytes = packet.Length;
            client.Connection.Send(packet);
        }

        _sim.World.ClearChanges();
    }

    /// <summary>Серверная сторона подключения клиента, для тестов.</summary>
    internal IConnection ClientConnection(int index) => _clients[index].Connection;

    /// <summary>Размер последнего пакета клиенту в байтах, для тестов и замеров.</summary>
    internal int LastFrameBytes(int index) => _clients[index].LastFrameBytes;

    /// <summary>Чанк, в котором лежит позиция.</summary>
    private static (int X, int Y) ChunkOf(CellPos cell) => (cell.X / World.ChunkSize, cell.Y / World.ChunkSize);

    /// <summary>Сообщение об изменении мира с текущим значением.</summary>
    private static ServerMessage ChangeMessage(World world, WorldChange change)
    {
        return change.Kind switch
        {
            WorldChangeKind.Floor => new FloorChangeMessage(change.Owner, world.Floor(change.Owner)),
            WorldChangeKind.NorthEdge => new EdgeChangeMessage(change.Owner, true, world.GetOwnedEdge(change.Owner, true)),
            _ => new EdgeChangeMessage(change.Owner, false, world.GetOwnedEdge(change.Owner, false)),
        };
    }

    /// <summary>Снимок чанка со всеми созданными слоями.</summary>
    private static ChunkSnapshotMessage Snapshot(World world, (int X, int Y) chunk)
    {
        List<ChunkLevelData> levels = [];
        foreach ((int z, ChunkLevel level) in world.ChunkLevels(chunk.X, chunk.Y))
        {
            ushort[] flags = [.. level.Flags.Select(flag => (ushort)flag)];
            levels.Add(new ChunkLevelData(z, [.. level.Floors], [.. level.NorthWalls], [.. level.WestWalls], flags));
        }

        return new ChunkSnapshotMessage(chunk.X, chunk.Y, levels);
    }

    /// <summary>Применение всех пришедших вводов клиента; неверный пакет разрывает подключение.</summary>
    private void ReceiveInputs(ClientSession client)
    {
        while (client.Connection.TryReceive(out byte[] packet))
        {
            try
            {
                InputMessage input = Protocol.ReadInput(packet);
                _sim.Entity(client.EntityId).Input = new MoveInput(input.X, input.Y);
                client.LastInput = input.Sequence;
            }
            catch (ProtocolException error)
            {
                _log.Error($"client {client.EntityId}: bad input packet, disconnecting: {error.Message}");
                client.Connection.Disconnect();
                return;
            }
        }
    }

    /// <summary>Пакет клиенту: выгрузки, изменения мира, снимки, сущности, свои статы.</summary>
    private ServerFrame BuildFrame(ClientSession client)
    {
        List<ServerMessage> messages = [];
        World world = _sim.World;
        Entity own = _sim.Entity(client.EntityId);
        if (!client.Welcomed)
        {
            messages.Add(new WelcomeMessage(client.EntityId, world.Width, world.Height));
            client.Welcomed = true;
        }

        HashSet<(int X, int Y)> wanted = WantedChunks(world, ChunkOf(own.Position.Cell));
        foreach ((int X, int Y) chunk in client.Chunks.Where(chunk => !wanted.Contains(chunk)).Order().ToList())
        {
            // Клиент сам забудет сущности выгруженного чанка, сервер забывает их так же
            messages.Add(new ChunkUnloadMessage(chunk.X, chunk.Y));
            client.Chunks.Remove(chunk);
            foreach (long id in client.Known.Where(pair => pair.Value.Chunk == chunk).Select(pair => pair.Key).ToList())
            {
                client.Known.Remove(id);
            }
        }

        // Изменения мира только для чанков, которые клиент уже знает: новые придут снимком с изменениями
        foreach (WorldChange change in world.Changes)
        {
            if (client.Chunks.Contains(ChunkOf(change.Owner)))
            {
                messages.Add(ChangeMessage(world, change));
            }
        }

        foreach ((int X, int Y) chunk in wanted.Where(chunk => !client.Chunks.Contains(chunk)).Order())
        {
            messages.Add(Snapshot(world, chunk));
            client.Chunks.Add(chunk);
        }

        AddEntityMessages(client, messages);

        float[] stats = [.. Enumerable.Range(0, _sim.StatTable.StatCount).Select(stat => (float)own.Stats.Value(stat))];
        if (client.Stats is null || !stats.AsSpan().SequenceEqual(client.Stats))
        {
            messages.Add(new OwnStatsMessage(stats));
            client.Stats = stats;
        }

        return new ServerFrame(_sim.Clock.Tick, messages);
    }

    /// <summary>Появления, движения, мудлы и исчезновения сущностей относительно того, что клиент уже знает.</summary>
    private void AddEntityMessages(ClientSession client, List<ServerMessage> messages)
    {
        HashSet<long> visible = [];
        foreach (Entity entity in _sim.Entities)
        {
            (int X, int Y) chunk = ChunkOf(entity.Position.Cell);
            if (!client.Chunks.Contains(chunk))
            {
                continue;
            }

            visible.Add(entity.Id);
            float x = (float)entity.Position.X;
            float y = (float)entity.Position.Y;
            int z = entity.Position.Z;
            if (!client.Known.TryGetValue(entity.Id, out KnownEntity? known))
            {
                int[] moodles = [.. Enumerable.Range(0, _sim.StatTable.MoodleCount).Select(entity.Stats.MoodleLevel)];
                messages.Add(new EntitySpawnMessage(entity.Id, x, y, z, moodles));
                client.Known[entity.Id] = new KnownEntity { X = x, Y = y, Z = z, Moodles = moodles, Chunk = chunk };
                continue;
            }

            if (known.X != x || known.Y != y || known.Z != z)
            {
                messages.Add(new EntityMoveMessage(entity.Id, x, y, z));
                (known.X, known.Y, known.Z, known.Chunk) = (x, y, z, chunk);
            }

            // Сравнение на месте: массив уровней на каждую сущность каждый тик был главным источником мусора
            for (int i = 0; i < known.Moodles.Length; i++)
            {
                int level = entity.Stats.MoodleLevel(i);
                if (known.Moodles[i] != level)
                {
                    messages.Add(new MoodleLevelMessage(entity.Id, i, level));
                    known.Moodles[i] = level;
                }
            }
        }

        foreach (long id in client.Known.Keys.Where(id => !visible.Contains(id)).Order().ToList())
        {
            messages.Add(new EntityRemoveMessage(id));
            client.Known.Remove(id);
        }
    }

    /// <summary>Чанки в квадрате радиуса вокруг центра, обрезанные по хранению мира.</summary>
    private HashSet<(int X, int Y)> WantedChunks(World world, (int X, int Y) center)
    {
        HashSet<(int X, int Y)> chunks = [];
        for (int y = Math.Max(0, center.Y - _radius); y <= Math.Min(world.ChunksY - 1, center.Y + _radius); y++)
        {
            for (int x = Math.Max(0, center.X - _radius); x <= Math.Min(world.ChunksX - 1, center.X + _radius); x++)
            {
                chunks.Add((x, y));
            }
        }

        return chunks;
    }

    /// <summary>Что клиент уже знает о сущности: последнее отправленное.</summary>
    private sealed class KnownEntity
    {
        /// <summary>Отправленная позиция по x.</summary>
        public float X { get; set; }

        /// <summary>Отправленная позиция по y.</summary>
        public float Y { get; set; }

        /// <summary>Отправленный этаж.</summary>
        public int Z { get; set; }

        /// <summary>Отправленные уровни мудлов.</summary>
        public required int[] Moodles { get; init; }

        /// <summary>Чанк по отправленной позиции.</summary>
        public (int X, int Y) Chunk { get; set; }
    }

    /// <summary>Подключённый клиент и то, что он уже знает.</summary>
    private sealed class ClientSession(IConnection connection, long entityId)
    {
        /// <summary>Подключение.</summary>
        public IConnection Connection { get; } = connection;

        /// <summary>Сущность клиента.</summary>
        public long EntityId { get; } = entityId;

        /// <summary>Отправлено ли приветствие.</summary>
        public bool Welcomed { get; set; }

        /// <summary>Номер последнего применённого ввода.</summary>
        public uint LastInput { get; set; }

        /// <summary>Чанки, которые клиент знает.</summary>
        public HashSet<(int X, int Y)> Chunks { get; } = [];

        /// <summary>Сущности, которые клиент знает.</summary>
        public Dictionary<long, KnownEntity> Known { get; } = [];

        /// <summary>Отправленные значения своих статов.</summary>
        public float[]? Stats { get; set; }

        /// <summary>Размер последнего пакета в байтах.</summary>
        public int LastFrameBytes { get; set; }
    }
}
