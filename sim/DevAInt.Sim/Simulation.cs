using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Lua-моды.</summary>
    private readonly LuaHost _lua;

    /// <summary>Задана ли карта: без неё мир пустой и позиции не проверяются.</summary>
    private readonly bool _hasMap;

    /// <summary>Id следующей сущности.</summary>
    private long _nextId = 1;

    /// <summary>Id следующего предмета.</summary>
    private long _nextItemId = 1;

    /// <summary>Sim по загруженным модам и карте mapId; без карты мир пустой 1 на 1. Ошибки скриптов в ScriptErrors.</summary>
    public Simulation(ModLoadResult mods, ISimLog log, string? mapId = null, GameClock? clock = null)
    {
        StatTable = new StatTable(mods.Defs);
        WorldTable = new WorldTable(mods.Defs);
        ItemTable = new ItemTable(mods.Defs);
        _hasMap = mapId is not null;
        MapDef? map = mapId is null ? null : mods.Defs.Get<MapDef>(mapId);
        World = map is null ? new World(1, 1) : MapBuilder.Build(map, WorldTable);
        Spawn = map is null ? WorldPos.CenterOf(new CellPos(0, 0, 0)) : WorldPos.CenterOf(new CellPos(map.Spawn.X, map.Spawn.Y, map.Spawn.Z));
        SpawnItems = map is null ? [] : [.. map.Spawn.Items.Select(mods.Defs.Get<ItemDef>)];

        // Постройка карты - не изменения для клиентов: они получат мир снимками чанков
        World.ClearChanges();
        Clock = clock ?? new GameClock();
        _lua = new LuaHost(StatTable, FindStats, log);
        ScriptErrors = _lua.Load(mods.Mods);
    }

    /// <summary>Игровое время.</summary>
    public GameClock Clock { get; }

    /// <summary>Мир.</summary>
    public World World { get; }

    /// <summary>Статы и мудлы по номерам.</summary>
    public StatTable StatTable { get; }

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

    /// <summary>Новая сущность со статами на минимуме; на карте позиция должна быть на полу.</summary>
    public long CreateEntity(WorldPos? at = null)
    {
        WorldPos position = at ?? WorldPos.CenterOf(new CellPos(0, 0, 0));
        if (_hasMap && !World.IsWalkable(position.Cell))
        {
            throw new ArgumentException($"position {position} is not on a floor", nameof(at));
        }

        Entity entity = new(_nextId++, new StatSet(StatTable), position);
        _entities[entity.Id] = entity;
        _order.Add(entity);
        return entity.Id;
    }

    /// <summary>Игрок в точке появления со стартовыми предметами карты, его id.</summary>
    public long SpawnPlayer()
    {
        long id = CreateEntity(Spawn);
        foreach (ItemDef item in SpawnItems)
        {
            GiveItem(id, item);
        }

        return id;
    }

    /// <summary>Новый предмет в инвентарь сущности, его id.</summary>
    public long GiveItem(long entityId, ItemDef def)
    {
        Item item = new(_nextItemId++, def);
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

    /// <summary>Тик: время, движение, действия, статы всех сущностей, хуки stat_threshold по сменам мудлов, хуки tick.</summary>
    public void Tick()
    {
        Clock.Advance();
        foreach (Entity entity in _order)
        {
            // Движение идёт в секундах симуляции, статы - в игровых минутах; ускорение действует на оба
            entity.Position = Movement.Step(World, entity.Position, entity.Input, Clock.SecondsPerTick, MoveParams.Default);

            // Действие до статов: мудлы от съеденного видны в этом же тике
            TickAction(entity);
            entity.Stats.Tick(Clock.MinutesPerTick);
        }

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
