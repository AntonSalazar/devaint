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

    /// <summary>Опущенные сейчас объекты.</summary>
    private HashSet<long> _cutObjects = [];

    /// <summary>Этаж, на котором сейчас срез.</summary>
    private int _cutLevel;

    /// <summary>Самый верхний видимый этаж, null - видны все.</summary>
    private int? _top;

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
        replica.ObjectChanged += OnObjectChanged;
        replica.EntitySpawned += OnEntitySpawned;
        replica.EntityMoved += OnEntityMoved;
        replica.EntityRemoved += OnEntityRemoved;
    }

    /// <summary>Узел чанка или null.</summary>
    public ChunkView? Chunk(int chunkX, int chunkY) => _chunks.GetValueOrDefault((chunkX, chunkY));

    /// <summary>Узел сущности или null.</summary>
    public EntityView? Entity(long id) => _entities.GetValueOrDefault(id);

    /// <summary>Срез стен и высоких объектов этажа своего персонажа под его ноги feet при взгляде камеры против back; null - персонажа нет, всё целое.</summary>
    public void UpdateCutaway(Vector3? feet, Vector3 back)
    {
        int level = feet is Vector3 height ? ViewMath.Level(height.Y) : 0;
        HashSet<WallKey> cut = feet is Vector3 at && _replica?.World is World world
            ? CutawayPlan.Compute(world, at, back, _chunks.Values.SelectMany(chunk => chunk.Level(level)?.Walls ?? []))
            : [];
        HashSet<long> cutObjects = feet is Vector3 body
            ? CutawayPlan.ComputeObjects(body, back, _chunks.Values.SelectMany(chunk => chunk.Level(level)?.Objects ?? []))
            : [];

        // Данные экземпляров пишутся, только когда план поменялся: персонаж чаще стоит или идёт вдоль комнаты
        if (!cut.SetEquals(_cut) || !cutObjects.SetEquals(_cutObjects) || level != _cutLevel)
        {
            _cut = cut;
            _cutObjects = cutObjects;
            _cutLevel = level;
            ApplyCut();
        }
    }

    /// <summary>Видны только этажи не выше top, как в изометрии; null - все. Меняет узлы, только когда граница сменилась.</summary>
    public void ShowUpTo(int? top)
    {
        if (top == _top)
        {
            return;
        }

        _top = top;
        foreach (ChunkView chunk in _chunks.Values)
        {
            chunk.ShowUpTo(top);
        }
    }

    /// <summary>Чужие существа видны, только если их клетка видна своему персонажу хотя бы наполовину; null - видно всё.</summary>
    public void UpdateVisibility(VisibilityMap? map)
    {
        foreach (EntityView entity in _entities.Values)
        {
            if (!entity.Own)
            {
                CellPos cell = new WorldPos(entity.Position.X, entity.Position.Z, ViewMath.Level(entity.Position.Y)).Cell;
                entity.SetSeen(map is null || map.Coverage(cell) >= VisibilityMap.VisibleThreshold);
            }
        }
    }

    /// <summary>Опущено ли ребро сейчас, для тестов.</summary>
    public bool IsCut(WallKey key) => _cut.Contains(key);

    /// <summary>Своя капсула смотрит по вводу движения; без ввода взгляд прежний.</summary>
    public void FaceOwn(MoveInput input) => Entity(_replica!.OwnEntityId)?.SetFacing(new Vector3((float)input.X, 0, (float)input.Y));

    /// <summary>Своя капсула идёт к предсказанной позиции.</summary>
    public void MoveOwn(WorldPos position) => Entity(_replica!.OwnEntityId)?.SetTarget(Place(position));

    /// <summary>Точка Godot для ног сущности: на лестнице выше пола этажа на долю подъёма.</summary>
    public Vector3 Place(WorldPos position)
    {
        float rise = _replica?.World is World world ? (float)Stairs.Rise(world, position) : 0;
        return ViewMath.ToGodot((float)position.X, (float)position.Y, position.Z) + (Vector3.Up * rise * ViewMath.FloorHeight);
    }

    /// <summary>Интерполяция всех сущностей на delta секунд и полосы действий; tickFraction - доля пути до следующего тика.</summary>
    public void Advance(double delta, double tickFraction = 0)
    {
        foreach (EntityView entity in _entities.Values)
        {
            entity.Advance(delta);
            ReplicaAction? action = _replica!.Entity(entity.EntityId)?.Action;

            // Между тиками прогресс досчитывается по доле времени, чтобы полоса шла плавно
            float? progress = action is null ? null : Mathf.Clamp((float)((_replica.Tick + tickFraction - action.StartTick) / action.DurationTicks), 0f, 1f);
            entity.SetActionProgress(progress, action?.Verb ?? string.Empty, action?.Subject ?? string.Empty);
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
        _replica.ObjectChanged -= OnObjectChanged;
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
        chunk.ShowUpTo(_top);
        chunk.Rebuild(_replica!.World!, _table!);
        chunk.ApplyCut(_cutLevel, _cut, _cutObjects);
    }

    /// <summary>Текущий срез во все чанки.</summary>
    private void ApplyCut()
    {
        foreach (ChunkView chunk in _chunks.Values)
        {
            chunk.ApplyCut(_cutLevel, _cut, _cutObjects);
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
            chunk.ApplyCut(_cutLevel, _cut, _cutObjects);
        }
    }

    /// <summary>Объект поставлен или убран: перестройка чанка его северо-западной клетки, где лежит его бокс.</summary>
    private void OnObjectChanged(WorldObject obj) => OnCellChanged(obj.Cell);

    /// <summary>Ребро изменилось: перестройка чанка клетки-владельца.</summary>
    private void OnEdgeChanged(CellPos owner, bool north) => OnCellChanged(owner);

    /// <summary>Новая сущность.</summary>
    private void OnEntitySpawned(ReplicaEntity entity)
    {
        EntityView view = new();
        view.Init(entity.Id, entity.Id == _replica!.OwnEntityId, Place(new WorldPos(entity.X, entity.Y, entity.Z)), ColorOf(entity));
        AddChild(view);
        _entities[entity.Id] = view;
    }

    /// <summary>Чужая сущность сдвинулась; своя двигается только по предсказанию через MoveOwn.</summary>
    private void OnEntityMoved(ReplicaEntity entity)
    {
        if (entity.Id != _replica!.OwnEntityId)
        {
            Entity(entity.Id)?.SetTarget(Place(new WorldPos(entity.X, entity.Y, entity.Z)));
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
