using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Saves;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Net;

/// <summary>Сервер: подключения, ввод клиентов, тик sim и пакет изменений каждому клиенту.</summary>
/// <remarks>Сервер над sim и транспортом.</remarks>
public sealed class GameServer(Simulation sim, IServerTransport transport, ISimLog log, SaveGame? save = null, int subscribeRadius = GameServer.DefaultSubscribeRadius)
{
    /// <summary>Автосохранение раз в 5 минут реального времени.</summary>
    public const int AutosaveTicks = 5 * 60 * GameClock.TicksPerSecond;

    /// <summary>Радиус подписки в чанках вокруг чанка своей сущности.</summary>
    public const int DefaultSubscribeRadius = 4;

    /// <summary>Наибольшее число вводов в очереди клиента: сверх этого старые выбрасываются, чтобы задержка не копилась.</summary>
    public const int MaxQueuedInputs = 3;

    /// <summary>Sim, который сервер ведёт.</summary>
    private readonly Simulation _sim = sim;

    /// <summary>Источник новых подключений.</summary>
    private readonly IServerTransport _transport = transport;

    /// <summary>Лог ошибок клиентов.</summary>
    private readonly ISimLog _log = log;

    /// <summary>Радиус подписки в чанках.</summary>
    private readonly int _radius = subscribeRadius;

    /// <summary>Сохранение мира, null - игра без сохранения.</summary>
    private readonly SaveGame? _save = save;

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
            _clients.Add(new ClientSession(connection));
        }

        foreach (ClientSession client in _clients)
        {
            ReceiveInputs(client);
            ApplyNextInput(client);
        }

        _sim.Tick();

        foreach (ClientSession client in _clients.Where(client => !client.Connection.IsConnected).ToList())
        {
            Leave(client);
        }

        Respawn();

        if (_save is not null && _sim.Clock.Tick % AutosaveTicks == 0)
        {
            Save();
        }

        // Клиент без опознания ещё не в мире, пакеты ему не нужны
        foreach (ClientSession client in _clients.Where(client => client.Joined))
        {
            byte[] packet = Protocol.Write(BuildFrame(client));
            client.LastFrameBytes = packet.Length;
            client.Connection.Send(packet);
        }

        _sim.World.ClearChanges();
    }

    /// <summary>Запись мира и игроков; ошибка диска пишется в лог, сервер работает дальше.</summary>
    public void Save()
    {
        if (_save is null)
        {
            return;
        }

        try
        {
            _save.Save(_sim);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Error($"save to {_save.Directory} failed: {error.Message}");
        }
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
            levels.Add(ChunkCodec.Copy(z, level));
        }

        return new ChunkSnapshotMessage(chunk.X, chunk.Y, levels);
    }

    /// <summary>Один ввод из очереди на тик; без ввода сущность идёт по прежнему.</summary>
    private void ApplyNextInput(ClientSession client)
    {
        // Клиент предсказывает движение по вводу на каждый тик, поэтому сервер тоже тратит ровно один ввод за тик,
        // даже если по сети пришло несколько сразу
        if (client.Joined && client.Inputs.TryDequeue(out InputMessage? input))
        {
            Entity entity = _sim.Entity(client.EntityId);
            entity.Input = new MoveInput(input.X, input.Y);
            entity.AimPoint = new WorldPos(input.AimX, input.AimY, entity.Position.Z);
            entity.FacesAim = input.FaceAim;
            client.LastInput = input.Sequence;
        }
    }

    /// <summary>Применение всех пришедших сообщений клиента; неверный пакет разрывает подключение.</summary>
    private void ReceiveInputs(ClientSession client)
    {
        while (client.Connection.TryReceive(out byte[] packet))
        {
            try
            {
                Apply(client, Protocol.ReadClientMessage(packet));
            }
            catch (ProtocolException error)
            {
                _log.Error($"client {client.PlayerId ?? "without hello"}: bad input packet, disconnecting: {error.Message}");
                client.Connection.Disconnect();
                return;
            }
        }
    }

    /// <summary>Одно сообщение клиента; на отказ начать действие клиент просто не увидит начала.</summary>
    private void Apply(ClientSession client, ClientMessage message)
    {
        if (message is HelloMessage hello)
        {
            Join(client, hello.PlayerId);
            return;
        }

        if (!client.Joined)
        {
            throw new ProtocolException("the first message must be hello");
        }

        switch (message)
        {
            case InputMessage input:
                client.Inputs.Enqueue(input);
                if (client.Inputs.Count > MaxQueuedInputs)
                {
                    client.Inputs.Dequeue();
                }

                break;
            case StartActionMessage start:
                if (start.Action >= _sim.ItemTable.ActionCount)
                {
                    throw new ProtocolException($"action {start.Action} is out of range");
                }

                _sim.StartAction(client.EntityId, start.ItemId, _sim.ItemTable.Action(start.Action).Id);
                break;
            case CancelActionMessage:
                _sim.CancelAction(client.EntityId);
                break;
            case ControlMessage control:
                if (control.Control >= _sim.ControlTable.Count)
                {
                    throw new ProtocolException($"control {control.Control} is out of range");
                }

                _sim.Control(client.EntityId, control.Control, control.Pressed);
                break;
        }
    }

    /// <summary>Опознание клиента: сохранённый персонаж игрока или новый в точке появления.</summary>
    private void Join(ClientSession client, string playerId)
    {
        if (client.Joined)
        {
            throw new ProtocolException("hello is sent twice");
        }

        if (!SaveGame.IsValidName(playerId))
        {
            throw new ProtocolException($"player id {playerId} is not valid");
        }

        // Один персонаж на игрока: второе подключение с тем же id получило бы ту же сущность
        if (_clients.Any(other => other.PlayerId == playerId))
        {
            throw new ProtocolException($"player {playerId} is already connected");
        }

        try
        {
            client.EntityId = _save?.LoadPlayer(_sim, playerId, _log) ?? _sim.SpawnPlayer(playerId);
        }
        catch (SaveException error)
        {
            throw new ProtocolException(error.Message);
        }

        client.PlayerId = playerId;
        _log.Info($"player {playerId} joined");
    }

    /// <summary>Игроки, чьи персонажи умерли на этом тике, получают новых в точке появления.</summary>
    private void Respawn()
    {
        foreach (Entity dead in _sim.Died)
        {
            if (_clients.Find(client => client.EntityId == dead.Id) is not ClientSession client)
            {
                continue;
            }

            // Новый персонаж сразу пишется в файл игрока, иначе после сбоя вернулся бы умерший
            client.EntityId = _sim.SpawnPlayer(client.PlayerId!);
            client.OwnChanged = true;
            client.Inputs.Clear();
            SavePlayer(client);
            _log.Info($"player {client.PlayerId} died");
        }
    }

    /// <summary>Запись персонажа игрока; ошибка диска - в лог.</summary>
    private void SavePlayer(ClientSession client)
    {
        try
        {
            _save?.SavePlayer(_sim, _sim.Entity(client.EntityId));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Error($"saving player {client.PlayerId} failed: {error.Message}");
        }
    }

    /// <summary>Клиент ушёл: его персонаж записывается и уходит из мира.</summary>
    private void Leave(ClientSession client)
    {
        _clients.Remove(client);
        if (!client.Joined)
        {
            return;
        }

        SavePlayer(client);
        _sim.RemoveEntity(client.EntityId);
        _log.Info($"player {client.PlayerId} left");
    }

    /// <summary>Пакет клиенту: выгрузки, изменения мира, снимки, сущности, свои статы.</summary>
    private ServerFrame BuildFrame(ClientSession client)
    {
        List<ServerMessage> messages = [];
        World world = _sim.World;
        Entity own = _sim.Entity(client.EntityId);
        if (!client.Welcomed)
        {
            messages.Add(new WelcomeMessage(client.EntityId, world.Width, world.Height, _sim.Clock.TimeScale, _sim.Mods));
            client.Welcomed = true;
        }

        // Смена своей сущности идёт до сущностей: новая придёт уже своей
        if (client.OwnChanged)
        {
            messages.Add(new OwnEntityMessage(client.EntityId));
            client.OwnChanged = false;
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

        if (client.InventoryVersion != own.Inventory.Version)
        {
            ItemData[] items = [.. own.Inventory.Items.Select(item => new ItemData(item.Id, _sim.ItemTable.ItemIndex(item.Def.Id)))];
            messages.Add(new OwnInventoryMessage(items));
            client.InventoryVersion = own.Inventory.Version;
        }

        float[] stats = [.. Enumerable.Range(0, _sim.StatTable.StatCount).Select(stat => (float)own.Stats.Value(stat))];
        if (client.Stats is null || !stats.AsSpan().SequenceEqual(client.Stats))
        {
            messages.Add(new OwnStatsMessage(stats));
            client.Stats = stats;
        }

        return new ServerFrame(_sim.Clock.Tick, messages, client.LastInput);
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
                int creature = entity.Creature is null ? -1 : _sim.CreatureTable.Index(entity.Creature.Id);
                messages.Add(new EntitySpawnMessage(entity.Id, x, y, z, creature, moodles));
                known = new KnownEntity { X = x, Y = y, Z = z, Moodles = moodles, Chunk = chunk };
                client.Known[entity.Id] = known;
                AddActionMessages(entity, known, messages);
                continue;
            }

            if (known.X != x || known.Y != y || known.Z != z)
            {
                messages.Add(new EntityMoveMessage(entity.Id, x, y, z));
                (known.X, known.Y, known.Z, known.Chunk) = (x, y, z, chunk);
            }

            AddActionMessages(entity, known, messages);

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

        // Удары до исчезновений: цель, убитая этим ударом, ещё известна клиенту
        foreach ((long attacker, long target, AttackKind kind) in _sim.Attacks)
        {
            if (client.Known.ContainsKey(attacker))
            {
                messages.Add(new AttackMessage(attacker, target, kind));
            }
        }

        foreach (long id in client.Known.Keys.Where(id => !visible.Contains(id)).Order().ToList())
        {
            messages.Add(new EntityRemoveMessage(id));
            client.Known.Remove(id);
        }
    }

    /// <summary>Начало и конец действия относительно того, что клиент знает; прогресс клиент считает сам по тикам.</summary>
    private void AddActionMessages(Entity entity, KnownEntity known, List<ServerMessage> messages)
    {
        long? start = entity.Action?.StartTick;
        if (start == known.ActionStart)
        {
            return;
        }

        if (known.ActionStart is not null)
        {
            messages.Add(new ActionEndedMessage(entity.Id, entity.LastActionEnd == ActionEnd.Completed));
        }

        if (entity.Action is ActionState action)
        {
            messages.Add(new ActionStartedMessage(entity.Id, _sim.ItemTable.ActionIndex(action.Action.Id), action.StartTick, action.DurationTicks));
        }

        known.ActionStart = start;
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

        /// <summary>Тик начала отправленного действия, null если клиент не знает о действии.</summary>
        public long? ActionStart { get; set; }
    }

    /// <summary>Подключённый клиент и то, что он уже знает.</summary>
    private sealed class ClientSession(IConnection connection)
    {
        /// <summary>Подключение.</summary>
        public IConnection Connection { get; } = connection;

        /// <summary>Сущность клиента, 0 до опознания.</summary>
        public long EntityId { get; set; }

        /// <summary>Id игрока, null до опознания.</summary>
        public string? PlayerId { get; set; }

        /// <summary>Своя сущность сменилась после смерти, клиенту нужно сообщить.</summary>
        public bool OwnChanged { get; set; }

        /// <summary>Клиент опознан и его персонаж в мире.</summary>
        public bool Joined => PlayerId is not null;

        /// <summary>Отправлено ли приветствие.</summary>
        public bool Welcomed { get; set; }

        /// <summary>Номер последнего применённого ввода.</summary>
        public uint LastInput { get; set; }

        /// <summary>Пришедшие, но ещё не применённые вводы.</summary>
        public Queue<InputMessage> Inputs { get; } = new();

        /// <summary>Чанки, которые клиент знает.</summary>
        public HashSet<(int X, int Y)> Chunks { get; } = [];

        /// <summary>Сущности, которые клиент знает.</summary>
        public Dictionary<long, KnownEntity> Known { get; } = [];

        /// <summary>Отправленные значения своих статов.</summary>
        public float[]? Stats { get; set; }

        /// <summary>Размер последнего пакета в байтах.</summary>
        public int LastFrameBytes { get; set; }

        /// <summary>Отправленный номер изменения инвентаря, -1 до первой отправки.</summary>
        public int InventoryVersion { get; set; } = -1;
    }
}
