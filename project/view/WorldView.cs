using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
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

    /// <summary>Виды существ по номерам, для цветов капсул.</summary>
    private CreatureTable? _creatures;

    /// <summary>Опущенные сейчас куски стен.</summary>
    private HashSet<WallKey> _cut = [];

    /// <summary>Число узлов чанков.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>Число узлов сущностей.</summary>
    public int EntityCount => _entities.Count;

    /// <summary>Подписка на реплику; вызывается сразу после создания, до первого пакета.</summary>
    public void Init(Replica replica, WorldTable table, CreatureTable creatures)
    {
        Name = "WorldView";
        _replica = replica;
        _table = table;
        _creatures = creatures;
        replica.Attacked += OnAttacked;
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

    /// <summary>Срез стен под ноги своего персонажа feet при взгляде камеры против back; null - персонажа нет, все стены целые.</summary>
    public void UpdateCutaway(Vector3? feet, Vector3 back)
    {
        HashSet<WallKey> cut = feet is Vector3 at && _replica?.World is World world
            ? CutawayPlan.Compute(world, at, back, _chunks.Values.SelectMany(chunk => chunk.Walls))
            : [];

        // Данные экземпляров пишутся, только когда план поменялся: персонаж чаще стоит или идёт вдоль комнаты
        if (!cut.SetEquals(_cut))
        {
            _cut = cut;
            ApplyCut();
        }
    }

    /// <summary>Опущено ли ребро сейчас, для тестов.</summary>
    public bool IsCut(WallKey key) => _cut.Contains(key);

    /// <summary>Своя капсула смотрит по вводу движения; без ввода взгляд прежний.</summary>
    public void FaceOwn(MoveInput input) => Entity(_replica!.OwnEntityId)?.SetFacing(new Vector3((float)input.X, 0, (float)input.Y));

    /// <summary>Своя капсула идёт к предсказанной позиции.</summary>
    public void MoveOwn(WorldPos position) => Entity(_replica!.OwnEntityId)?.SetTarget(ViewMath.ToGodot((float)position.X, (float)position.Y, position.Z));

    /// <summary>Интерполяция всех сущностей на delta секунд и полосы действий; tickFraction - доля пути до следующего тика.</summary>
    public void Advance(double delta, double tickFraction = 0)
    {
        foreach (EntityView entity in _entities.Values)
        {
            entity.Advance(delta);
            ReplicaAction? action = _replica!.Entity(entity.EntityId)?.Action;

            // Между тиками прогресс досчитывается по доле времени, чтобы полоса шла плавно
            float? progress = action is null ? null : Mathf.Clamp((float)((_replica.Tick + tickFraction - action.StartTick) / action.DurationTicks), 0f, 1f);
            entity.SetActionProgress(progress);
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
        _replica.Attacked -= OnAttacked;
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
        chunk.ApplyCut(_cut);
    }

    /// <summary>Текущий срез во все чанки.</summary>
    private void ApplyCut()
    {
        foreach (ChunkView chunk in _chunks.Values)
        {
            chunk.ApplyCut(_cut);
        }
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
    private void OnCellChanged(CellPos cell)
    {
        if (Chunk(cell.X / World.ChunkSize, cell.Y / World.ChunkSize) is ChunkView chunk)
        {
            chunk.Rebuild(_replica!.World!, _table!);
            chunk.ApplyCut(_cut);
        }
    }

    /// <summary>Ребро изменилось: перестройка чанка клетки-владельца.</summary>
    private void OnEdgeChanged(CellPos owner, bool north) => OnCellChanged(owner);

    /// <summary>Новая сущность.</summary>
    private void OnEntitySpawned(ReplicaEntity entity)
    {
        EntityView view = new();
        view.Init(entity.Id, entity.Id == _replica!.OwnEntityId, ViewMath.ToGodot(entity.X, entity.Y, entity.Z), ColorOf(entity));
        AddChild(view);
        _entities[entity.Id] = view;
    }

    /// <summary>Чужая сущность сдвинулась; своя двигается только по предсказанию через MoveOwn.</summary>
    private void OnEntityMoved(ReplicaEntity entity)
    {
        if (entity.Id != _replica!.OwnEntityId)
        {
            Entity(entity.Id)?.SetTarget(ViewMath.ToGodot(entity.X, entity.Y, entity.Z));
        }
    }

    /// <summary>Удар или толчок прошёл: выпад атакующего, а при ударе ещё и вспышка цели, если они видны.</summary>
    private void OnAttacked(long attacker, long target, AttackKind kind)
    {
        Entity(attacker)?.PlayAttack();
        if (kind == AttackKind.Strike)
        {
            Entity(target)?.PlayHit();
        }
    }

    /// <summary>Цвет капсулы из вида существа; у сущности без вида - серый.</summary>
    private Color ColorOf(ReplicaEntity entity) => entity.Creature >= 0 ? Color.FromHtml(_creatures!.Creature(entity.Creature).Color) : new Color(0.6f, 0.6f, 0.6f);

    /// <summary>Сущность исчезла.</summary>
    private void OnEntityRemoved(ReplicaEntity entity)
    {
        if (_entities.Remove(entity.Id, out EntityView? view))
        {
            Drop(view);
        }
    }
}
