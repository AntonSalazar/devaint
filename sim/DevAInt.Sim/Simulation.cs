using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Lua;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim;

/// <summary>Ядро sim: время, мир, сущности со статами и движением, Lua-моды.</summary>
public sealed class Simulation
{
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

    /// <summary>Sim по загруженным модам и карте mapId; без карты мир пустой 1 на 1. Ошибки скриптов в ScriptErrors.</summary>
    public Simulation(ModLoadResult mods, ISimLog log, string? mapId = null, GameClock? clock = null)
    {
        StatTable = new StatTable(mods.Defs);
        WorldTable = new WorldTable(mods.Defs);
        _hasMap = mapId is not null;
        World = mapId is null ? new World(1, 1) : MapBuilder.Build(mods.Defs.Get<MapDef>(mapId), WorldTable);
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

    /// <summary>Ошибки загрузки Lua-скриптов модов.</summary>
    public IReadOnlyList<ModError> ScriptErrors { get; }

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

    /// <summary>Сущность по id, исключение если её нет.</summary>
    public Entity Entity(long id) => _entities[id];

    /// <summary>Тик: время, движение, статы всех сущностей, хуки stat_threshold по сменам мудлов, хуки tick.</summary>
    public void Tick()
    {
        Clock.Advance();
        foreach (Entity entity in _order)
        {
            // Движение идёт в реальных секундах, статы - в игровых минутах
            entity.Position = Movement.Step(World, entity.Position, entity.Input, GameClock.SecondsPerTick, MoveParams.Default);
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

    /// <summary>Статы сущности или null.</summary>
    private StatSet? FindStats(long id) => _entities.GetValueOrDefault(id)?.Stats;
}
