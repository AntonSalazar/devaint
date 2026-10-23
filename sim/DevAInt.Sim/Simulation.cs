using System.Collections.Generic;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Lua;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim;

/// <summary>Ядро sim: время, сущности со статами и Lua-моды; пока без мира.</summary>
public sealed class Simulation
{
    /// <summary>Статы сущностей по id.</summary>
    private readonly Dictionary<long, StatSet> _entities = [];

    /// <summary>Id сущностей в порядке создания, чтобы тик шёл всегда в одном порядке.</summary>
    private readonly List<long> _order = [];

    /// <summary>Lua-моды.</summary>
    private readonly LuaHost _lua;

    /// <summary>Id следующей сущности.</summary>
    private long _nextId = 1;

    /// <summary>Sim по загруженным модам; ошибки скриптов в ScriptErrors.</summary>
    public Simulation(ModLoadResult mods, ISimLog log, GameClock? clock = null)
    {
        StatTable = new StatTable(mods.Defs);
        Clock = clock ?? new GameClock();
        _lua = new LuaHost(StatTable, FindStats, log);
        ScriptErrors = _lua.Load(mods.Mods);
    }

    /// <summary>Игровое время.</summary>
    public GameClock Clock { get; }

    /// <summary>Статы и мудлы по номерам.</summary>
    public StatTable StatTable { get; }

    /// <summary>Ошибки загрузки Lua-скриптов модов.</summary>
    public IReadOnlyList<ModError> ScriptErrors { get; }

    /// <summary>Новая сущность со статами на минимуме, её id.</summary>
    public long CreateEntity()
    {
        long id = _nextId++;
        _entities[id] = new StatSet(StatTable);
        _order.Add(id);
        return id;
    }

    /// <summary>Статы сущности, исключение если её нет.</summary>
    public StatSet EntityStats(long id) => _entities[id];

    /// <summary>Тик: время, статы всех сущностей, хуки stat_threshold по сменам мудлов, хуки tick.</summary>
    public void Tick()
    {
        Clock.Advance();
        foreach (long id in _order)
        {
            _entities[id].Tick(Clock.MinutesPerTick);
        }

        // Хуки идут после всех статов: мод видит согласованное состояние тика
        foreach (long id in _order)
        {
            foreach (MoodleChange change in _entities[id].Changes)
            {
                _lua.RaiseStatThreshold(id, change);
            }
        }

        _lua.RaiseTick();
    }

    /// <summary>Статы сущности или null.</summary>
    private StatSet? FindStats(long id) => _entities.GetValueOrDefault(id);
}
