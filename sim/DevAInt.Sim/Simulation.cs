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

namespace DevAInt.Sim;

/// <summary>Ядро sim: время, мир, сущности со статами, движением и действиями, Lua-моды.</summary>
public sealed class Simulation
{
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

    /// <summary>Удары на последнем тике.</summary>
    private readonly List<(long Attacker, long Target)> _attacks = [];

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
        Mods = [.. mods.Mods.Select(mod => mod.Info)];
        _hasMap = mapId is not null;
        MapId = mapId;
        MapDef? map = mapId is null ? null : mods.Defs.Get<MapDef>(mapId);
        World = world ?? (map is null ? new World(1, 1) : MapBuilder.Build(map, WorldTable));
        Spawn = map is null ? WorldPos.CenterOf(new CellPos(0, 0, 0)) : WorldPos.CenterOf(new CellPos(map.Spawn.X, map.Spawn.Y, map.Spawn.Z));
        SpawnItems = map is null ? [] : [.. map.Spawn.Items.Select(mods.Defs.Get<ItemDef>)];
        SpawnCreature = map?.Spawn.Creature;

        // Постройка карты - не изменения для клиентов: они получат мир снимками чанков
        World.ClearChanges();
        Clock = clock ?? new GameClock();
        _lua = new LuaHost(StatTable, FindStats, log);
        ScriptErrors = _lua.Load(mods.Mods);

        // Существа карты появляются только в новом мире: в сохранённом они уже есть
        if (world is null && map is not null)
        {
            SpawnMapCreatures(map, log);
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

    /// <summary>Вид существа игроков с карты, null - сущность без вида.</summary>
    public string? SpawnCreature { get; }

    /// <summary>Сущности, умершие на последнем тике, уже убраны из мира.</summary>
    public IReadOnlyList<Entity> Died => _died;

    /// <summary>Удары, которые прошли на последнем тике: кто и кого.</summary>
    public IReadOnlyList<(long Attacker, long Target)> Attacks => _attacks;

    /// <summary>Полы и стены по номерам.</summary>
    public WorldTable WorldTable { get; }

    /// <summary>Предметы и действия по номерам.</summary>
    public ItemTable ItemTable { get; }

    /// <summary>Ошибки загрузки Lua-скриптов модов.</summary>
    public IReadOnlyList<ModError> ScriptErrors { get; }

    /// <summary>Точка появления игроков: центр клетки spawn карты.</summary>
    public WorldPos Spawn { get; }

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
        long id = CreateEntity(Spawn, SpawnCreature);
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
            case ControlAction.UseItem:
                UseFirstItem(entity);
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
            entity.Position = Movement.Step(World, entity.Position, entity.Input, Clock.SecondsPerTick, entity.Body);
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
    internal void RecordAttack(Entity attacker, Entity target) => _attacks.Add((attacker.Id, target.Id));

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

    /// <summary>Взгляд за точкой прицела, если сущность целится, иначе по направлению движения.</summary>
    private static void UpdateFacing(Entity entity)
    {
        if (entity.Aiming && entity.AimPoint is WorldPos aim)
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
        foreach (Entity entity in _order.Where(entity => entity.AttackRequested))
        {
            entity.AttackRequested = false;
            if (Combat.FrontTarget(this, entity) is Entity target)
            {
                Combat.TryAttack(this, entity, target);
            }
        }
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

        foreach (StatChangeDef change in action.Use.Stats)
        {
            int stat = StatTable.StatIndex(change.Stat);
            entity.Stats.SetValue(stat, entity.Stats.Value(stat) + change.Add);
        }

        if (action.Use.Consume)
        {
            entity.Inventory.Remove(action.ItemId);
        }

        entity.Action = null;
        entity.LastActionEnd = ActionEnd.Completed;
    }

    /// <summary>Статы сущности или null.</summary>
    private StatSet? FindStats(long id) => _entities.GetValueOrDefault(id)?.Stats;
}
