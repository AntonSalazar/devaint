using System;
using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Net;

/// <summary>Сущность в реплике клиента: позиция и уровни мудлов, без правил.</summary>
public sealed class ReplicaEntity(long id, float x, float y, int z, int[] moodleLevels)
{
    /// <summary>Id сущности на сервере.</summary>
    public long Id { get; } = id;

    /// <summary>Позиция по x в метрах.</summary>
    public float X { get; internal set; } = x;

    /// <summary>Позиция по y в метрах.</summary>
    public float Y { get; internal set; } = y;

    /// <summary>Этаж.</summary>
    public int Z { get; internal set; } = z;

    /// <summary>Уровни мудлов по номерам из StatTable.</summary>
    public int[] MoodleLevels { get; } = moodleLevels;
}

/// <summary>Копия мира на клиенте: применяет пакеты сервера и сообщает об изменениях событиями.</summary>
public sealed class Replica(StatTable stats)
{
    /// <summary>Сущности по id.</summary>
    private readonly Dictionary<long, ReplicaEntity> _entities = [];

    /// <summary>Чанки, которые клиент знает.</summary>
    private readonly HashSet<(int X, int Y)> _chunks = [];

    /// <summary>Мир пришёл: приветствие получено.</summary>
    public event Action? WorldCreated;

    /// <summary>Чанк загружен из снимка.</summary>
    public event Action<int, int>? ChunkLoaded;

    /// <summary>Чанк выгружен.</summary>
    public event Action<int, int>? ChunkUnloaded;

    /// <summary>Сущность появилась.</summary>
    public event Action<ReplicaEntity>? EntitySpawned;

    /// <summary>Сущность сдвинулась.</summary>
    public event Action<ReplicaEntity>? EntityMoved;

    /// <summary>Уровень мудла сущности сменился: сущность, номер мудла, старый уровень.</summary>
    public event Action<ReplicaEntity, int, int>? MoodleChanged;

    /// <summary>Сущность исчезла.</summary>
    public event Action<ReplicaEntity>? EntityRemoved;

    /// <summary>Статы своей сущности изменились.</summary>
    public event Action? OwnStatsChanged;

    /// <summary>Пол клетки изменился.</summary>
    public event Action<CellPos>? FloorChanged;

    /// <summary>Ребро изменилось: клетка-владелец и true для северного ребра.</summary>
    public event Action<CellPos, bool>? EdgeChanged;

    /// <summary>Мир клиента, null до приветствия.</summary>
    public World? World { get; private set; }

    /// <summary>Своя сущность, 0 до приветствия.</summary>
    public long OwnEntityId { get; private set; }

    /// <summary>Номер последнего применённого тика сервера.</summary>
    public long Tick { get; private set; }

    /// <summary>Значения статов своей сущности по номерам из StatTable.</summary>
    public float[] OwnStats { get; } = new float[stats.StatCount];

    /// <summary>Сущности, которые клиент сейчас видит.</summary>
    public IReadOnlyCollection<ReplicaEntity> Entities => _entities.Values;

    /// <summary>Чанки, которые клиент сейчас знает.</summary>
    public IReadOnlyCollection<(int X, int Y)> Chunks => _chunks;

    /// <summary>Сущность по id или null.</summary>
    public ReplicaEntity? Entity(long id) => _entities.GetValueOrDefault(id);

    /// <summary>Применение пакета сервера; данные вне мира или неверные номера - ProtocolException.</summary>
    public void Apply(ServerFrame frame)
    {
        Tick = frame.Tick;
        foreach (ServerMessage message in frame.Messages)
        {
            try
            {
                Apply(message);
            }
            catch (ArgumentException error)
            {
                throw new ProtocolException($"message {message.GetType().Name} does not fit the world: {error.Message}");
            }
        }
    }

    /// <summary>Чанк, в котором стоит сущность.</summary>
    private static (int X, int Y) ChunkOf(ReplicaEntity entity)
    {
        CellPos cell = new WorldPos(entity.X, entity.Y, entity.Z).Cell;
        return (cell.X / World.ChunkSize, cell.Y / World.ChunkSize);
    }

    /// <summary>Номер чанка внутри хранения мира.</summary>
    private static void RequireChunk(World world, int chunkX, int chunkY)
    {
        if (chunkX < 0 || chunkY < 0 || chunkX >= world.ChunksX || chunkY >= world.ChunksY)
        {
            throw new ProtocolException($"chunk {chunkX},{chunkY} is outside the world");
        }
    }

    /// <summary>Одно сообщение сервера.</summary>
    private void Apply(ServerMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                World = new World(welcome.Width, welcome.Height);
                OwnEntityId = welcome.EntityId;
                WorldCreated?.Invoke();
                break;
            case ChunkSnapshotMessage snapshot:
                LoadChunk(snapshot);
                break;
            case ChunkUnloadMessage unload:
                UnloadChunk(unload.ChunkX, unload.ChunkY);
                break;
            case EntitySpawnMessage spawn:
                SpawnEntity(spawn);
                break;
            case EntityMoveMessage move:
                ReplicaEntity moved = Existing(move.Id);
                (moved.X, moved.Y, moved.Z) = (move.X, move.Y, move.Z);
                EntityMoved?.Invoke(moved);
                break;
            case MoodleLevelMessage moodle:
                ReplicaEntity target = Existing(moodle.Id);
                if (moodle.Moodle >= target.MoodleLevels.Length)
                {
                    throw new ProtocolException($"moodle {moodle.Moodle} is out of range");
                }

                int old = target.MoodleLevels[moodle.Moodle];
                target.MoodleLevels[moodle.Moodle] = moodle.Level;
                MoodleChanged?.Invoke(target, moodle.Moodle, old);
                break;
            case EntityRemoveMessage remove:
                ReplicaEntity removed = Existing(remove.Id);
                _entities.Remove(remove.Id);
                EntityRemoved?.Invoke(removed);
                break;
            case OwnStatsMessage ownStats:
                if (ownStats.Values.Count != OwnStats.Length)
                {
                    throw new ProtocolException($"got {ownStats.Values.Count} stats, expected {OwnStats.Length}");
                }

                ownStats.Values.ToArray().CopyTo(OwnStats, 0);
                OwnStatsChanged?.Invoke();
                break;
            case FloorChangeMessage floor:
                RequireWorld().SetFloor(floor.Cell, floor.Floor);
                FloorChanged?.Invoke(floor.Cell);
                break;
            case EdgeChangeMessage edge:
                RequireWorld().SetOwnedEdge(edge.Owner, edge.North, edge.Edge);
                EdgeChanged?.Invoke(edge.Owner, edge.North);
                break;
            default:
                throw new ProtocolException($"unknown message {message.GetType().Name}");
        }
    }

    /// <summary>Заполнение чанка из снимка.</summary>
    private void LoadChunk(ChunkSnapshotMessage snapshot)
    {
        World world = RequireWorld();
        RequireChunk(world, snapshot.ChunkX, snapshot.ChunkY);
        world.UnloadChunk(snapshot.ChunkX, snapshot.ChunkY);
        foreach (ChunkLevelData data in snapshot.Levels)
        {
            if (data.Z is < World.MinLevel or > World.MaxLevel)
            {
                throw new ProtocolException($"level {data.Z} is out of range");
            }

            ChunkLevel level = world.LoadChunkLevel(snapshot.ChunkX, snapshot.ChunkY, data.Z);
            data.Floors.CopyTo(level.Floors, 0);
            data.NorthWalls.CopyTo(level.NorthWalls, 0);
            data.WestWalls.CopyTo(level.WestWalls, 0);
            for (int i = 0; i < data.Flags.Length; i++)
            {
                level.Flags[i] = (byte)data.Flags[i];
            }
        }

        _chunks.Add((snapshot.ChunkX, snapshot.ChunkY));
        ChunkLoaded?.Invoke(snapshot.ChunkX, snapshot.ChunkY);
    }

    /// <summary>Выгрузка чанка и сущностей, которые в нём стоят.</summary>
    private void UnloadChunk(int chunkX, int chunkY)
    {
        World world = RequireWorld();
        RequireChunk(world, chunkX, chunkY);
        foreach (ReplicaEntity entity in _entities.Values.Where(entity => ChunkOf(entity) == (chunkX, chunkY)).ToList())
        {
            _entities.Remove(entity.Id);
            EntityRemoved?.Invoke(entity);
        }

        world.UnloadChunk(chunkX, chunkY);
        _chunks.Remove((chunkX, chunkY));
        ChunkUnloaded?.Invoke(chunkX, chunkY);
    }

    /// <summary>Новая сущность.</summary>
    private void SpawnEntity(EntitySpawnMessage spawn)
    {
        if (spawn.MoodleLevels.Count != stats.MoodleCount)
        {
            throw new ProtocolException($"got {spawn.MoodleLevels.Count} moodles, expected {stats.MoodleCount}");
        }

        ReplicaEntity entity = new(spawn.Id, spawn.X, spawn.Y, spawn.Z, [.. spawn.MoodleLevels]);
        if (!_entities.TryAdd(spawn.Id, entity))
        {
            throw new ProtocolException($"entity {spawn.Id} already exists");
        }

        EntitySpawned?.Invoke(entity);
    }

    /// <summary>Известная сущность или ProtocolException.</summary>
    private ReplicaEntity Existing(long id) => _entities.GetValueOrDefault(id) ?? throw new ProtocolException($"unknown entity {id}");

    /// <summary>Мир после приветствия или ProtocolException.</summary>
    private World RequireWorld() => World ?? throw new ProtocolException("message before welcome");
}
