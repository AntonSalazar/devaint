using System;
using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Controls;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Lua;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;
using DevAInt.Sim.Ui;

namespace DevAInt.Sim;

/// <summary>Ядро sim: время, мир, сущности со статами, движением и действиями, Lua-моды.</summary>
public sealed class Simulation
{
    /// <summary>Действие, которым идут постройка и разбор.</summary>
    public const string BuildAction = "core:build";

    /// <summary>Движение короче этого не прерывает действия: стик геймпада в покое чуть дрожит.</summary>
    private const double MoveThreshold = 0.01;

    /// <summary>Сущности по id.</summary>
    private readonly Dictionary<long, Entity> _entities = [];

    /// <summary>Сущности в порядке создания, чтобы тик шёл всегда в одном порядке.</summary>
    private readonly List<Entity> _order = [];

    /// <summary>Сущности по видам существ на текущем тике, для поиска целей.</summary>
    private readonly Dictionary<string, List<Entity>> _byCreature = [];

    /// <summary>Общий список целей нескольких видов, чтобы не выделять память каждый тик.</summary>
    private readonly List<Entity> _candidates = [];

    /// <summary>Умершие на последнем тике.</summary>
    private readonly List<Entity> _died = [];

    /// <summary>Случай для выбора точки появления, когда моды не выбрали; зерно постоянное, чтобы игра была повторяемой.</summary>
    private readonly Random _random = new(0);

    /// <summary>Удары на последнем тике.</summary>
    private readonly List<(long Attacker, long Target, AttackKind Kind)> _attacks = [];

    /// <summary>Lua-моды.</summary>
    private readonly LuaHost _lua;

    /// <summary>Задана ли карта: без неё мир пустой и позиции не проверяются.</summary>
    private readonly bool _hasMap;

    /// <summary>Sim по загруженным модам и карте mapId; без карты мир пустой 1 на 1. Ошибки скриптов в ScriptErrors.</summary>
    public Simulation(ModLoadResult mods, ISimLog log, string? mapId = null, GameClock? clock = null)
        : this(mods, log, mapId, clock, world: null)
    {
    }

    /// <summary>Sim с готовым миром из сохранения вместо постройки карты; карта нужна для точки появления.</summary>
    internal Simulation(ModLoadResult mods, ISimLog log, string? mapId, GameClock? clock, World? world)
    {
        StatTable = new StatTable(mods.Defs);
        WorldTable = new WorldTable(mods.Defs);
        ItemTable = new ItemTable(mods.Defs);
        CreatureTable = new CreatureTable(mods.Defs);
        ControlTable = new ControlTable(mods.Defs);
        UiTable = new UiTable(mods.Defs);
        Mods = [.. mods.Mods.Select(mod => mod.Info)];
        _hasMap = mapId is not null;
        MapId = mapId;
        MapDef? map = mapId is null ? null : mods.Defs.Get<MapDef>(mapId);
        World = world ?? (map is null ? new World(1, 1) : MapBuilder.Build(map, WorldTable, log));
        SpawnPoints = map is null ? [new CellPos(0, 0, 0)] : [.. map.Spawn.Points.Select(point => new CellPos(point.X, point.Y, point.Z))];
        SpawnItems = map is null ? [] : [.. map.Spawn.Items.Select(mods.Defs.Get<ItemDef>)];
        SpawnCreature = map?.Spawn.Creature;

        // Постройка карты - не изменения для клиентов: они получат мир снимками чанков
        World.ClearChanges();
        Clock = clock ?? new GameClock();
        _lua = new LuaHost(StatTable, FindStats, () => _order, this, log);
        ScriptErrors = _lua.Load(mods.Mods);

        // Существа и предметы контейнеров карты появляются только в новом мире: в сохранённом они уже есть
        if (world is null && map is not null)
        {
            SpawnMapCreatures(map, log);
            FillMapContainers(map, mods.Defs);
        }
    }

    /// <summary>Карта, из которой sim начат, null без карты.</summary>
    public string? MapId { get; }

    /// <summary>Моды sim в порядке загрузки, для сверки с клиентами.</summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    /// <summary>Игровое время.</summary>
    public GameClock Clock { get; }

    /// <summary>Мир.</summary>
    public World World { get; }

    /// <summary>Статы и мудлы по номерам.</summary>
    public StatTable StatTable { get; }

    /// <summary>Виды существ по номерам.</summary>
    public CreatureTable CreatureTable { get; }

    /// <summary>Действия управления по номерам.</summary>
    public ControlTable ControlTable { get; }

    /// <summary>Панели интерфейса модов и действия кнопок по номерам.</summary>
    public UiTable UiTable { get; }

    /// <summary>Вид существа игроков с карты, null - сущность без вида.</summary>
    public string? SpawnCreature { get; }

    /// <summary>Сущности, умершие на последнем тике, уже убраны из мира.</summary>
    public IReadOnlyList<Entity> Died => _died;

    /// <summary>Удары, которые прошли на последнем тике: кто и кого.</summary>
    public IReadOnlyList<(long Attacker, long Target, AttackKind Kind)> Attacks => _attacks;

    /// <summary>Полы и стены по номерам.</summary>
    public WorldTable WorldTable { get; }

    /// <summary>Предметы и действия по номерам.</summary>
    public ItemTable ItemTable { get; }

    /// <summary>Ошибки загрузки Lua-скриптов модов.</summary>
    public IReadOnlyList<ModError> ScriptErrors { get; }

    /// <summary>Клетки появления игроков с карты.</summary>
    public IReadOnlyList<CellPos> SpawnPoints { get; }

    /// <summary>Предметы, с которыми появляется игрок.</summary>
    public IReadOnlyList<ItemDef> SpawnItems { get; }

    /// <summary>Сущности в порядке создания.</summary>
    internal IReadOnlyList<Entity> Entities => _order;

    /// <summary>Id следующей сущности.</summary>
    internal long NextEntityId { get; set; } = 1;

    /// <summary>Поиск пути с переиспользуемыми коллекциями, общий для всех существ.</summary>
    internal Pathfinder Paths { get; } = new();

    /// <summary>Id следующего предмета.</summary>
    internal long NextItemId { get; set; } = 1;

    /// <summary>Значения интерфейса для всех игроков от Lua по ключу.</summary>
    internal Dictionary<string, string> GlobalUi { get; } = [];

    /// <summary>Номер изменения общих значений интерфейса.</summary>
    internal int GlobalUiVersion { get; private set; }

    /// <summary>Новая сущность вида creatureId или без вида со статами на начальных значениях; на карте позиция должна быть на полу.</summary>
    public long CreateEntity(WorldPos? at = null, string? creatureId = null)
    {
        WorldPos position = at ?? WorldPos.CenterOf(new CellPos(0, 0, 0));
        if (_hasMap && !World.IsWalkable(position.Cell))
        {
            throw new ArgumentException($"position {position} is not on a floor", nameof(at));
        }

        CreatureDef? creature = creatureId is null ? null : CreatureTable.Creature(CreatureTable.Index(creatureId));
        Entity entity = new(NextEntityId++, new StatSet(StatTable, creature), position, creature)
        {
            Brain = creature?.Brain is null ? null : new BrainState(),
        };
        _entities[entity.Id] = entity;
        _order.Add(entity);
        return entity.Id;
    }

    /// <summary>Новый персонаж игрока playerId в точке появления со стартовыми предметами карты, id сущности.</summary>
    public long SpawnPlayer(string playerId)
    {
        long id = CreateEntity(WorldPos.CenterOf(ChooseSpawn(playerId)), SpawnCreature);
        _entities[id].PlayerId = playerId;
        foreach (ItemDef item in SpawnItems)
        {
            GiveItem(id, item);
        }

        return id;
    }

    /// <summary>Новый предмет в инвентарь сущности, его id.</summary>
    public long GiveItem(long entityId, ItemDef def)
    {
        Item item = new(NextItemId++, def);
        _entities[entityId].Inventory.Add(item);
        return item.Id;
    }

    /// <summary>Новый объект вида objectId в клетке cell с поворотом rotation; null и причина в problem, если вида нет или места нет.</summary>
    public WorldObject? PlaceObject(string objectId, CellPos cell, int rotation, out string? problem)
    {
        if (!WorldTable.TryObjectIndex(objectId, out ushort index))
        {
            problem = $"unknown object {objectId}";
            return null;
        }

        WorldObject obj = WorldObject.Create(World.NextObjectId, index, WorldTable.Object(index), cell, rotation);
        problem = World.Contains(cell) ? World.PlaceProblem(obj) : $"cell {cell.X},{cell.Y},{cell.Z} is outside the world";
        if (problem is not null)
        {
            return null;
        }

        World.PlaceObject(obj);
        return obj;
    }

    /// <summary>Новый предмет вида itemId в контейнер объекта; null и причина в problem, если вида нет, объект не контейнер или места нет.</summary>
    public long? AddToContainer(long objectId, string itemId, out string? problem)
    {
        if (!ItemTable.TryItemIndex(itemId, out int index))
        {
            problem = $"unknown item {itemId}";
            return null;
        }

        ItemDef def = ItemTable.Item(index);
        if (World.Container(objectId) is not Inventory container)
        {
            problem = $"object {objectId} is not a container";
            return null;
        }

        if (container.Weight + def.Weight > World.FindObject(objectId)!.Capacity + 1e-9)
        {
            problem = $"object {objectId} is full";
            return null;
        }

        Item item = new(NextItemId++, def);
        container.Add(item);
        MarkContainerDirty(objectId);
        problem = null;
        return item.Id;
    }

    /// <summary>Удаление предмета из контейнера объекта, false если предмета там нет.</summary>
    public bool RemoveFromContainer(long objectId, long itemId)
    {
        if (World.Container(objectId)?.Remove(itemId) != true)
        {
            return false;
        }

        MarkContainerDirty(objectId);
        return true;
    }

    /// <summary>Перекладывание предмета между своим инвентарём, 0, и контейнерами объектов по id: оба места в досягаемости, у цели хватает вместимости.</summary>
    public ItemMove MoveItem(long entityId, long itemId, long from, long to)
    {
        Entity entity = _entities[entityId];
        if (from == to)
        {
            return ItemMove.SamePlace;
        }

        if (Place(entity, from, out Inventory? source, out _) is ItemMove sourceProblem)
        {
            return sourceProblem;
        }

        if (Place(entity, to, out Inventory? target, out double capacity) is ItemMove targetProblem)
        {
            return targetProblem;
        }

        if (source!.Find(itemId) is not Item item)
        {
            return ItemMove.NoItem;
        }

        if (entity.Action?.ItemId == itemId)
        {
            return ItemMove.InUse;
        }

        // Малый запас на сумму дробных весов: десять яблок по 0.2 ровно влезают в 2 кг
        if (target!.Weight + item.Def.Weight > capacity + 1e-9)
        {
            return ItemMove.Full;
        }

        source.Remove(itemId);
        target.Add(item);
        MarkContainerDirty(from);
        MarkContainerDirty(to);
        return ItemMove.Moved;
    }

    /// <summary>Попытка начать действие предметом; действие идёт с текущего тика.</summary>
    public ActionStart StartAction(long entityId, long itemId, string actionId)
    {
        Entity entity = _entities[entityId];
        if (entity.Action is not null)
        {
            return ActionStart.Busy;
        }

        if (entity.Inventory.Find(itemId) is not Item item)
        {
            return ActionStart.NoItem;
        }

        ItemUseDef? use = item.Def.Uses.FirstOrDefault(candidate => candidate.Action == actionId);
        if (use is null)
        {
            return ActionStart.NoUse;
        }

        // Длительность в тиках по секундам симуляции: ускорение времени ускоряет и действия
        int ticks = Math.Max(1, (int)Math.Ceiling(use.Seconds / Clock.SecondsPerTick));
        entity.Action = new ActionState(ItemTable.Action(ItemTable.ActionIndex(actionId)), use, itemId, Clock.Tick, ticks);
        return ActionStart.Started;
    }

    /// <summary>Попытка начать крафт по рецепту: входы и инструменты в своём инвентаре, результат влезает по весу.</summary>
    public ActionStart StartCraft(long entityId, string recipeId)
    {
        Entity entity = _entities[entityId];
        if (entity.Action is not null)
        {
            return ActionStart.Busy;
        }

        if (!ItemTable.TryRecipeIndex(recipeId, out int index))
        {
            return ActionStart.NoUse;
        }

        RecipeDef recipe = ItemTable.Recipe(index);
        if (CraftProblem(entity, recipe) is ActionStart problem)
        {
            return problem;
        }

        int ticks = Math.Max(1, (int)Math.Ceiling(recipe.Seconds / Clock.SecondsPerTick));
        entity.Action = new ActionState(ItemTable.Action(ItemTable.ActionIndex(recipe.Action)), null, 0, Clock.Tick, ticks) { Recipe = recipe };
        return ActionStart.Started;
    }

    /// <summary>Значение интерфейса key для игрока entity или для всех при null; value null убирает значение.</summary>
    public void SetUiValue(long? entityId, string key, string? value)
    {
        Dictionary<string, string> values = entityId is long id ? _entities[id].UiValues : GlobalUi;
        bool changed = value is null ? values.Remove(key) : !values.TryGetValue(key, out string? old) || old != value;
        if (!changed)
        {
            return;
        }

        if (value is not null)
        {
            values[key] = value;
        }

        if (entityId is long owner)
        {
            _entities[owner].UiVersion++;
        }
        else
        {
            GlobalUiVersion++;
        }
    }

    /// <summary>Кнопка интерфейса нажата игроком сущности: хук ui_action у модов с id действия.</summary>
    public void UiAction(long entityId, int action) => _lua.RaiseUiAction(entityId, UiTable.Action(action));

    /// <summary>Открыть или закрыть ближнюю створку впереди на расстоянии руки; закрыть дверь нельзя, если в проёме кто-то стоит; true - створка сдвинулась.</summary>
    public bool Interact(long entityId)
    {
        Entity entity = _entities[entityId];
        if (Building.NearestFill(World, entity.Position, entity.Facing) is not (CellPos cell, Side side))
        {
            return false;
        }

        Edge edge = World.GetEdge(cell, side);
        bool blocked = edge.Open && edge.Kind == WallKind.DoorFrame && _order.Any(other => other.Position.Z == cell.Z && Building.SegmentDistance(other.Position, cell, side) < other.Body.Radius);
        if (blocked)
        {
            return false;
        }

        World.SetEdge(cell, side, edge with { Open = !edge.Open });
        return true;
    }

    /// <summary>Попытка начать постройку или разбор: место по правилам Building, материалы и инструменты в своём инвентаре, возврат разбора влезает по весу.</summary>
    public ActionStart StartBuild(long entityId, BuildTarget target)
    {
        Entity entity = _entities[entityId];
        if (entity.Action is not null)
        {
            return ActionStart.Busy;
        }

        if (!ItemTable.TryActionIndex(BuildAction, out int action))
        {
            return ActionStart.NoUse;
        }

        if (BuildProblem(entity, target) is ActionStart problem)
        {
            return problem;
        }

        BuildDef def = Building.Def(World, WorldTable, target)!;
        int ticks = Math.Max(1, (int)Math.Ceiling(def.Seconds / Clock.SecondsPerTick));
        entity.Action = new ActionState(ItemTable.Action(action), null, 0, Clock.Tick, ticks) { Build = target };
        return ActionStart.Started;
    }

    /// <summary>Действие управления нажато или отпущено игроком сущности; действие, которому нужно другое зажатое, без него не срабатывает.</summary>
    public void Control(long entityId, int control, bool pressed)
    {
        Entity entity = _entities[entityId];
        ControlDef def = ControlTable.Control(control);
        if (!pressed)
        {
            entity.HeldControls.Remove(control);
            entity.Aiming = IsAimHeld(entity);
            return;
        }

        if (def.Needs is string needed && !entity.HeldControls.Contains(ControlTable.Index(needed)))
        {
            return;
        }

        if (def.Hold)
        {
            entity.HeldControls.Add(control);
        }

        switch (def.Does)
        {
            case ControlAction.Aim:
                entity.Aiming = true;
                break;
            case ControlAction.Attack:
                entity.AttackRequested = true;
                break;
            case ControlAction.Shove:
                entity.ShoveRequested = true;
                break;
            case ControlAction.UseItem:
                UseFirstItem(entity);
                break;
            case ControlAction.Interact:
                Interact(entityId);
                break;
            case ControlAction.Lua:
                WorldPos aim = entity.AimPoint ?? entity.Position;
                _lua.RaiseControl(entity.Id, def.Id, aim.X, aim.Y);
                break;
        }
    }

    /// <summary>Просьба атаковать на следующем тике ближайшую сущность впереди.</summary>
    public void Attack(long entityId) => _entities[entityId].AttackRequested = true;

    /// <summary>Отмена идущего действия, false если действия нет.</summary>
    public bool CancelAction(long entityId)
    {
        Entity entity = _entities[entityId];
        if (entity.Action is null)
        {
            return false;
        }

        entity.Action = null;
        entity.LastActionEnd = ActionEnd.Interrupted;
        return true;
    }

    /// <summary>Сущность по id, исключение если её нет.</summary>
    public Entity Entity(long id) => _entities[id];

    /// <summary>Удаление сущности, false если её нет.</summary>
    public bool RemoveEntity(long id)
    {
        if (!_entities.Remove(id, out Entity? entity))
        {
            return false;
        }

        _order.Remove(entity);
        return true;
    }

    /// <summary>Тик: время, поведение, движение, действия, статы, атаки, смерти, хуки stat_threshold по сменам мудлов, хуки tick.</summary>
    public void Tick()
    {
        Clock.Advance();
        _died.Clear();
        _attacks.Clear();
        ThinkAll();
        foreach (Entity entity in _order)
        {
            // Движение идёт в секундах симуляции, статы - в игровых минутах; ускорение действует на оба
            entity.Position = Move(entity);
            UpdateFacing(entity);

            // Действие до статов: мудлы от съеденного видны в этом же тике
            TickAction(entity);
            entity.Stats.Tick(Clock.MinutesPerTick);
        }

        AttackAll();
        RemoveDead();

        // Хуки идут после всех сущностей: мод видит согласованное состояние тика
        foreach (Entity entity in _order)
        {
            foreach (MoodleChange change in entity.Stats.Changes)
            {
                _lua.RaiseStatThreshold(entity.Id, change);
            }
        }

        _lua.RaiseTick();
    }

    /// <summary>Запись прошедшего удара для рассылки клиентам.</summary>
    internal void RecordAttack(Entity attacker, Entity target, AttackKind kind) => _attacks.Add((attacker.Id, target.Id, kind));

    /// <summary>Сущность из сохранения со своим id; счётчики id сдвигаются за неё и её предметы.</summary>
    internal void RestoreEntity(Entity entity)
    {
        _entities.Add(entity.Id, entity);
        _order.Add(entity);
        NextEntityId = Math.Max(NextEntityId, entity.Id + 1);
        foreach (Item item in entity.Inventory.Items)
        {
            NextItemId = Math.Max(NextItemId, item.Id + 1);
        }
    }

    /// <summary>Взгляд за точкой прицела, если сущность целится или просит смотреть на неё, иначе по направлению движения.</summary>
    private static void UpdateFacing(Entity entity)
    {
        if ((entity.Aiming || entity.FacesAim) && entity.AimPoint is WorldPos aim)
        {
            double dx = aim.X - entity.Position.X;
            double dy = aim.Y - entity.Position.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance > MoveThreshold)
            {
                entity.Facing = (dx / distance, dy / distance);
            }

            return;
        }

        MoveInput input = entity.Input.Clamped();
        double length = Math.Sqrt((input.X * input.X) + (input.Y * input.Y));
        if (length > MoveThreshold)
        {
            entity.Facing = (input.X / length, input.Y / length);
        }
    }

    /// <summary>Точка появления игрока: ответ хука choose_spawn или случайная точка карты.</summary>
    private CellPos ChooseSpawn(string playerId) => _lua.ChooseSpawn(playerId, SpawnPoints) is int chosen ? SpawnPoints[chosen] : SpawnPoints[_random.Next(SpawnPoints.Count)];

    /// <summary>Стартовые предметы в контейнеры объектов карты, которые встали на место.</summary>
    private void FillMapContainers(MapDef map, DefDatabase defs)
    {
        foreach (MapOpDef op in map.Build.Where(op => op.Object is not null && op.Items.Count > 0))
        {
            // Объект без места пропущен постройкой, тогда в клетке другой объект или ничего
            WorldObject? obj = World.ObjectAt(new CellPos(op.X, op.Y, op.Z));
            if (obj is null || obj.Cell != new CellPos(op.X, op.Y, op.Z) || World.Container(obj.Id) is not Inventory container)
            {
                continue;
            }

            foreach (string item in op.Items)
            {
                container.Add(new Item(NextItemId++, defs.Get<ItemDef>(item)));
            }
        }
    }

    /// <summary>Инвентарь места: своё, 0, или контейнер объекта в досягаемости; null - место годится, иначе причина отказа.</summary>
    private ItemMove? Place(Entity entity, long place, out Inventory? inventory, out double capacity)
    {
        if (place == 0)
        {
            inventory = entity.Inventory;
            capacity = entity.Creature?.Inventory?.Capacity ?? double.PositiveInfinity;
            return null;
        }

        inventory = World.Container(place);
        capacity = World.FindObject(place)?.Capacity ?? 0;
        if (inventory is null)
        {
            return ItemMove.NoContainer;
        }

        return Reach.CanReach(World, entity.Position, World.FindObject(place)!) ? null : ItemMove.OutOfReach;
    }

    /// <summary>Регион контейнера нужно переписать при сохранении; своё место, 0, лежит в сущности.</summary>
    private void MarkContainerDirty(long place)
    {
        if (place != 0 && World.FindObject(place) is WorldObject obj)
        {
            World.MarkRegionDirty(World.RegionOf(obj.Cell));
        }
    }

    /// <summary>Существа карты в центрах своих клеток; клетка без пола - ошибка в лог, существа пропускаются.</summary>
    private void SpawnMapCreatures(MapDef map, ISimLog log)
    {
        foreach (CreatureSpawnDef spawn in map.Creatures)
        {
            WorldPos position = WorldPos.CenterOf(new CellPos(spawn.X, spawn.Y, spawn.Z));
            if (!World.IsWalkable(position.Cell))
            {
                log.Error($"map {map.Id}: creature {spawn.Creature} at {spawn.X},{spawn.Y},{spawn.Z} is not on a floor");
                continue;
            }

            for (int i = 0; i < spawn.Count; i++)
            {
                CreateEntity(position, spawn.Creature);
            }
        }
    }

    /// <summary>Первое применение первого предмета, у которого оно есть.</summary>
    private void UseFirstItem(Entity entity)
    {
        if (entity.Inventory.Items.FirstOrDefault(item => item.Def.Uses.Count > 0) is Item item)
        {
            StartAction(entity.Id, item.Id, item.Def.Uses[0].Action);
        }
    }

    /// <summary>Зажато ли ещё какое-нибудь действие прицеливания.</summary>
    private bool IsAimHeld(Entity entity) => entity.HeldControls.Any(held => ControlTable.Control(held).Does == ControlAction.Aim);

    /// <summary>Решения поведения всех существ с поведением.</summary>
    private void ThinkAll()
    {
        // Цели собираются по видам один раз за тик, а не перебором всех сущностей каждым существом
        foreach (List<Entity> list in _byCreature.Values)
        {
            list.Clear();
        }

        foreach (Entity entity in _order.Where(entity => entity.Creature is not null))
        {
            if (!_byCreature.TryGetValue(entity.Creature!.Id, out List<Entity>? list))
            {
                _byCreature[entity.Creature.Id] = list = [];
            }

            list.Add(entity);
        }

        foreach (Entity entity in _order)
        {
            if (entity.Creature?.Brain is BrainDef brain)
            {
                if (IsStunned(entity))
                {
                    entity.Input = MoveInput.None;
                    continue;
                }

                Brain.Think(this, entity, brain, Candidates(brain));
            }
        }
    }

    /// <summary>Сущности видов-целей поведения.</summary>
    private IReadOnlyList<Entity> Candidates(BrainDef brain)
    {
        if (brain.Targets.Count == 1)
        {
            return _byCreature.GetValueOrDefault(brain.Targets[0]) ?? [];
        }

        _candidates.Clear();
        foreach (string target in brain.Targets)
        {
            _candidates.AddRange(_byCreature.GetValueOrDefault(target) ?? []);
        }

        return _candidates;
    }

    /// <summary>Атаки, которые попросили игроки: ближайшая сущность впереди.</summary>
    private void AttackAll()
    {
        foreach (Entity entity in _order.Where(entity => entity.AttackRequested || entity.ShoveRequested))
        {
            // Оглушённый ни бить, ни толкать не может, а просьба на этот тик пропадает
            bool stunned = IsStunned(entity);
            if (entity.AttackRequested && !stunned && Combat.StrikeTarget(this, entity) is Entity target)
            {
                Combat.TryAttack(this, entity, target);
            }

            if (entity.ShoveRequested && !stunned && entity.Creature?.Shove is ShoveDef shove
                && Combat.FrontTarget(this, entity, shove.Range, _ => true) is Entity shoved)
            {
                Combat.TryShove(this, entity, shoved);
            }

            entity.AttackRequested = false;
            entity.ShoveRequested = false;
        }
    }

    /// <summary>Оглушена ли сущность на текущем тике.</summary>
    private bool IsStunned(Entity entity) => Clock.Tick < entity.StunnedUntilTick;

    /// <summary>Новая позиция за тик: отлёт после толчка, оглушённый стоит, остальные идут по вводу.</summary>
    private WorldPos Move(Entity entity)
    {
        if (entity.Knock is (double x, double y, double speed, long until))
        {
            if (Clock.Tick <= until)
            {
                // Отлёт идёт через то же движение, поэтому стена останавливает его, как и ходьбу
                return Movement.Step(World, entity.Position, new MoveInput(x, y), Clock.SecondsPerTick, entity.Body with { Speed = speed });
            }

            entity.Knock = null;
        }

        return IsStunned(entity) ? entity.Position : Movement.Step(World, entity.Position, entity.Input, Clock.SecondsPerTick, entity.Body);
    }

    /// <summary>Смерть существ, у которых смертельный стат дошёл до порога: они уходят из мира в Died.</summary>
    private void RemoveDead()
    {
        foreach (Entity entity in _order.Where(entity => entity.Stats.ReachedDeath()).ToList())
        {
            RemoveEntity(entity.Id);
            _died.Add(entity);
        }
    }

    /// <summary>Прерывание действия движением или его окончание: статы и трата предмета.</summary>
    private void TickAction(Entity entity)
    {
        if (entity.Action is not ActionState action)
        {
            return;
        }

        MoveInput input = entity.Input.Clamped();
        bool moving = (input.X * input.X) + (input.Y * input.Y) > MoveThreshold * MoveThreshold;
        if (moving && action.Action.InterruptOnMove)
        {
            CancelAction(entity.Id);
            return;
        }

        if (Clock.Tick < action.EndTick)
        {
            return;
        }

        entity.Action = null;
        entity.LastActionEnd = action switch
        {
            { Recipe: RecipeDef recipe } => CompleteRecipe(entity, recipe),
            { Build: BuildTarget target } => CompleteBuild(entity, target),
            _ => CompleteUse(entity, action),
        };
    }

    /// <summary>Окончание постройки или разбора: всё проверяется заново, потом материалы тратятся или возвращаются и мир меняется.</summary>
    private ActionEnd CompleteBuild(Entity entity, BuildTarget target)
    {
        if (BuildProblem(entity, target) is not null)
        {
            return ActionEnd.Interrupted;
        }

        BuildDef def = Building.Def(World, WorldTable, target)!;
        switch (target.Kind)
        {
            case BuildKind.Object:
                Crafting.Take(entity.Inventory, def.Inputs);
                World.PlaceObject(WorldObject.Create(World.NextObjectId, target.Def, WorldTable.Object(target.Def), target.Cell, target.Turn));
                break;
            case BuildKind.Wall:
                Crafting.Take(entity.Inventory, def.Inputs);
                World.SetEdge(target.Cell, target.Side, new Edge(target.Def, false, 0, WorldTable.Wall(target.Def).Kind));
                break;
            case BuildKind.Fill:
                Crafting.Take(entity.Inventory, def.Inputs);
                World.SetEdge(target.Cell, target.Side, World.GetEdge(target.Cell, target.Side) with { Fill = target.Def, Open = false });
                break;
            case BuildKind.RemoveFill:
                World.SetEdge(target.Cell, target.Side, World.GetEdge(target.Cell, target.Side) with { Fill = 0, Open = false });
                GiveAll(entity, Building.Refund(def));
                break;
            case BuildKind.RemoveObject:
                World.RemoveObject(World.ObjectAt(target.Cell)!.Id);
                GiveAll(entity, Building.Refund(def));
                break;
            default:
                World.SetEdge(target.Cell, target.Side, Edge.None);
                GiveAll(entity, Building.Refund(def));
                break;
        }

        return ActionEnd.Completed;
    }

    /// <summary>Почему постройку или разбор нельзя начать или закончить: место, материалы и инструменты, вес возврата; null - можно.</summary>
    private ActionStart? BuildProblem(Entity entity, BuildTarget target)
    {
        IEnumerable<(WorldPos Position, double Radius)> bodies = _order.Select(other => (other.Position, other.Body.Radius));
        if (Building.Problem(World, WorldTable, target, entity.Position, bodies) is not null)
        {
            return ActionStart.BadPlace;
        }

        BuildDef def = Building.Def(World, WorldTable, target)!;
        bool removing = target.Kind is BuildKind.RemoveObject or BuildKind.RemoveWall or BuildKind.RemoveFill;
        if (Crafting.Missing(removing ? [] : def.Inputs, def.Tools, Crafting.Counts(entity.Inventory)) is not null)
        {
            return ActionStart.NoItem;
        }

        double capacity = entity.Creature?.Inventory?.Capacity ?? double.PositiveInfinity;
        bool fits = !removing || entity.Inventory.Weight + Crafting.Weight(Building.Refund(def), ItemTable) <= capacity + 1e-9;
        return fits ? null : ActionStart.Full;
    }

    /// <summary>Новые предметы по списку в инвентарь сущности.</summary>
    private void GiveAll(Entity entity, IReadOnlyList<ItemCountDef> items)
    {
        foreach (ItemCountDef entry in items)
        {
            ItemDef def = ItemTable.Item(ItemTable.ItemIndex(entry.Item));
            for (int i = 0; i < entry.Count; i++)
            {
                entity.Inventory.Add(new Item(NextItemId++, def));
            }
        }
    }

    /// <summary>Окончание применения предмета: статы и трата предмета.</summary>
    private ActionEnd CompleteUse(Entity entity, ActionState action)
    {
        foreach (StatChangeDef change in action.Use!.Stats)
        {
            int stat = StatTable.StatIndex(change.Stat);
            entity.Stats.SetValue(stat, entity.Stats.Value(stat) + change.Add);
        }

        if (action.Use.Consume)
        {
            entity.Inventory.Remove(action.ItemId);
        }

        return ActionEnd.Completed;
    }

    /// <summary>Окончание крафта: входы тратятся, выходы появляются; если за время крафта материалы ушли или место кончилось - прервано.</summary>
    private ActionEnd CompleteRecipe(Entity entity, RecipeDef recipe)
    {
        if (CraftProblem(entity, recipe) is not null)
        {
            return ActionEnd.Interrupted;
        }

        Crafting.Take(entity.Inventory, recipe.Inputs);
        foreach (ItemCountDef output in recipe.Outputs)
        {
            ItemDef def = ItemTable.Item(ItemTable.ItemIndex(output.Item));
            for (int i = 0; i < output.Count; i++)
            {
                entity.Inventory.Add(new Item(NextItemId++, def));
            }
        }

        return ActionEnd.Completed;
    }

    /// <summary>Почему крафт по рецепту нельзя начать или закончить: нет материалов или результат не влезет; null - можно.</summary>
    private ActionStart? CraftProblem(Entity entity, RecipeDef recipe)
    {
        if (Crafting.Missing(recipe.Inputs, recipe.Tools, Crafting.Counts(entity.Inventory)) is not null)
        {
            return ActionStart.NoItem;
        }

        double capacity = entity.Creature?.Inventory?.Capacity ?? double.PositiveInfinity;
        double after = entity.Inventory.Weight - Crafting.Weight(recipe.Inputs, ItemTable) + Crafting.Weight(recipe.Outputs, ItemTable);
        return after > capacity + 1e-9 ? ActionStart.Full : null;
    }

    /// <summary>Статы сущности или null.</summary>
    private StatSet? FindStats(long id) => _entities.GetValueOrDefault(id)?.Stats;
}
