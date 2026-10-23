using System.Collections.Generic;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Net;
using Godot;

/// <summary>Картинка мира по реплике: узлы чанков и сущностей; ничего не решает, только рисует.</summary>
public partial class WorldView : Node3D
{
    /// <summary>Узлы чанков.</summary>
    private readonly Dictionary<(int X, int Y), ChunkView> _chunks = [];

    /// <summary>Узлы сущностей.</summary>
    private readonly Dictionary<long, EntityView> _entities = [];

    /// <summary>Реплика, на события которой подписан вид.</summary>
    private Replica? _replica;

    /// <summary>Полы и стены по номерам, для цветов.</summary>
    private WorldTable? _table;

    /// <summary>Число узлов чанков.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Число узлов сущностей.</summary>
    public int EntityCount => _entities.Count;

    /// <summary>Подписка на реплику; вызывается сразу после создания, до первого пакета.</summary>
    public void Init(Replica replica, WorldTable table)
    {
        Name = "WorldView";
        _replica = replica;
        _table = table;
        replica.ChunkLoaded += OnChunkLoaded;
        replica.ChunkUnloaded += OnChunkUnloaded;
        replica.FloorChanged += OnCellChanged;
        replica.EdgeChanged += OnEdgeChanged;
        replica.EntitySpawned += OnEntitySpawned;
        replica.EntityMoved += OnEntityMoved;
        replica.EntityRemoved += OnEntityRemoved;
    }

    /// <summary>Узел чанка или null.</summary>
    public ChunkView? Chunk(int chunkX, int chunkY) => _chunks.GetValueOrDefault((chunkX, chunkY));

    /// <summary>Узел сущности или null.</summary>
    public EntityView? Entity(long id) => _entities.GetValueOrDefault(id);

    /// <summary>Интерполяция всех сущностей на delta секунд.</summary>
    public void Advance(double delta)
    {
        foreach (EntityView entity in _entities.Values)
        {
            entity.Advance(delta);
        }
    }

    /// <summary>Отписка от реплики при выходе из дерева.</summary>
    public override void _ExitTree()
    {
        if (_replica is null)
        {
            return;
        }

        _replica.ChunkLoaded -= OnChunkLoaded;
        _replica.ChunkUnloaded -= OnChunkUnloaded;
        _replica.FloorChanged -= OnCellChanged;
        _replica.EdgeChanged -= OnEdgeChanged;
        _replica.EntitySpawned -= OnEntitySpawned;
        _replica.EntityMoved -= OnEntityMoved;
        _replica.EntityRemoved -= OnEntityRemoved;
    }

    /// <summary>Узел удаляется из вида и освобождается.</summary>
    private void Drop(Node node)
    {
        RemoveChild(node);
        node.QueueFree();
    }

    /// <summary>Новый чанк.</summary>
    private void OnChunkLoaded(int chunkX, int chunkY)
    {
        ChunkView chunk = new();
        chunk.Init(chunkX, chunkY);
        AddChild(chunk);
        _chunks[(chunkX, chunkY)] = chunk;
        chunk.Rebuild(_replica!.World!, _table!);
    }

    /// <summary>Чанк вышел из зоны.</summary>
    private void OnChunkUnloaded(int chunkX, int chunkY)
    {
        if (_chunks.Remove((chunkX, chunkY), out ChunkView? chunk))
        {
            Drop(chunk);
        }
    }

    /// <summary>Пол изменился: перестройка его чанка.</summary>
    private void OnCellChanged(CellPos cell) => Chunk(cell.X / World.ChunkSize, cell.Y / World.ChunkSize)?.Rebuild(_replica!.World!, _table!);

    /// <summary>Ребро изменилось: перестройка чанка клетки-владельца.</summary>
    private void OnEdgeChanged(CellPos owner, bool north) => OnCellChanged(owner);

    /// <summary>Новая сущность.</summary>
    private void OnEntitySpawned(ReplicaEntity entity)
    {
        EntityView view = new();
        view.Init(entity.Id, entity.Id == _replica!.OwnEntityId, ViewMath.ToGodot(entity.X, entity.Y, entity.Z));
        AddChild(view);
        _entities[entity.Id] = view;
    }

    /// <summary>Сущность сдвинулась.</summary>
    private void OnEntityMoved(ReplicaEntity entity) => Entity(entity.Id)?.SetTarget(ViewMath.ToGodot(entity.X, entity.Y, entity.Z));

    /// <summary>Сущность исчезла.</summary>
    private void OnEntityRemoved(ReplicaEntity entity)
    {
        if (_entities.Remove(entity.Id, out EntityView? view))
        {
            Drop(view);
        }
    }
}
