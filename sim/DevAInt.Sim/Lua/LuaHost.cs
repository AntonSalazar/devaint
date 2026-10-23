using System;
using System.Collections.Generic;
using System.IO;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Stats;
using MoonSharp.Interpreter;

namespace DevAInt.Sim.Lua;

/// <summary>Lua-моды сервера: загрузка скриптов, API для модов и вызов хуков.</summary>
internal sealed class LuaHost
{
    /// <summary>Хук каждого тика, без аргументов.</summary>
    public const string TickHook = "tick";

    /// <summary>Хук смены уровня мудла: entity, moodle, old_level, new_level.</summary>
    public const string StatThresholdHook = "stat_threshold";

    /// <summary>Известные хуки.</summary>
    private static readonly HashSet<string> _hooks = [TickHook, StatThresholdHook];

    /// <summary>Таблица статов для перевода id в номера.</summary>
    private readonly StatTable _table;

    /// <summary>Статы сущности по id, null если её нет.</summary>
    private readonly Func<long, StatSet?> _entities;

    /// <summary>Лог для ошибок и print.</summary>
    private readonly ISimLog _log;

    /// <summary>Lua-моды в порядке загрузки.</summary>
    private readonly List<LuaMod> _mods = [];

    /// <summary>Id мудлов как Lua-строки, чтобы не создавать их на каждое событие.</summary>
    private readonly DynValue[] _moodleIds;

    /// <summary>Хост с доступом к статам сущностей.</summary>
    public LuaHost(StatTable table, Func<long, StatSet?> entities, ISimLog log)
    {
        _table = table;
        _entities = entities;
        _log = log;
        _moodleIds = new DynValue[table.MoodleCount];
        for (int i = 0; i < table.MoodleCount; i++)
        {
            _moodleIds[i] = DynValue.NewString(table.Moodle(i).Def.Id);
        }
    }

    /// <summary>Загрузка скриптов модов, у которых есть папка scripts; ошибки загрузки в ответе.</summary>
    public IReadOnlyList<ModError> Load(IReadOnlyList<ModManifest> mods)
    {
        List<ModError> errors = [];
        foreach (ModManifest mod in mods)
        {
            if (!Directory.Exists(Path.Combine(mod.Directory, "scripts")))
            {
                continue;
            }

            LuaMod lua = new(mod.Id, _log);
            Bind(lua);
            lua.LoadScripts(mod, errors);
            _mods.Add(lua);
        }

        return errors;
    }

    /// <summary>Хук tick у всех модов.</summary>
    public void RaiseTick()
    {
        foreach (LuaMod mod in _mods)
        {
            mod.Raise(TickHook, DynValue.Nil, DynValue.Nil, DynValue.Nil, DynValue.Nil);
        }
    }

    /// <summary>Хук stat_threshold у всех модов.</summary>
    public void RaiseStatThreshold(long entity, MoodleChange change)
    {
        if (_mods.Count == 0)
        {
            return;
        }

        DynValue id = DynValue.NewNumber(entity);
        DynValue oldLevel = DynValue.NewNumber(change.OldLevel);
        DynValue newLevel = DynValue.NewNumber(change.NewLevel);
        foreach (LuaMod mod in _mods)
        {
            mod.Raise(StatThresholdHook, id, _moodleIds[change.Moodle], oldLevel, newLevel);
        }
    }

    /// <summary>hooks.add: имя хука и функция-обработчик.</summary>
    private static DynValue AddHook(LuaMod mod, CallbackArguments args)
    {
        string hook = args.AsType(0, "hooks.add", DataType.String).String;
        DynValue handler = args.AsType(1, "hooks.add", DataType.Function);
        if (!_hooks.Contains(hook))
        {
            throw new ScriptRuntimeException($"unknown hook {hook}, known hooks: {string.Join(", ", _hooks)}");
        }

        mod.AddHandler(hook, handler);
        return DynValue.Nil;
    }

    /// <summary>Таблицы hooks и stats в Lua-состоянии мода.</summary>
    private void Bind(LuaMod mod)
    {
        Table hooks = new(mod.Script);
        hooks["add"] = DynValue.NewCallback((context, args) => AddHook(mod, args), "hooks.add");
        mod.Script.Globals["hooks"] = hooks;

        Table stats = new(mod.Script);
        stats["get"] = DynValue.NewCallback((context, args) => StatsGet(args), "stats.get");
        stats["set"] = DynValue.NewCallback((context, args) => StatsSet(args), "stats.set");
        stats["moodle"] = DynValue.NewCallback((context, args) => StatsMoodle(args), "stats.moodle");
        mod.Script.Globals["stats"] = stats;
    }

    /// <summary>stats.get: значение стата сущности.</summary>
    private DynValue StatsGet(CallbackArguments args)
    {
        StatSet stats = Entity(args, "stats.get");
        return DynValue.NewNumber(stats.Value(Stat(args, "stats.get")));
    }

    /// <summary>stats.set: установка значения стата в пределах диапазона.</summary>
    private DynValue StatsSet(CallbackArguments args)
    {
        StatSet stats = Entity(args, "stats.set");
        int stat = Stat(args, "stats.set");
        stats.SetValue(stat, args.AsType(2, "stats.set", DataType.Number).Number);
        return DynValue.Nil;
    }

    /// <summary>stats.moodle: уровень мудла сущности, 0 если мудла нет.</summary>
    private DynValue StatsMoodle(CallbackArguments args)
    {
        StatSet stats = Entity(args, "stats.moodle");
        string id = args.AsType(1, "stats.moodle", DataType.String).String;
        if (!_table.TryMoodleIndex(id, out int moodle))
        {
            throw new ScriptRuntimeException($"unknown moodle {id}");
        }

        return DynValue.NewNumber(stats.MoodleLevel(moodle));
    }

    /// <summary>Статы сущности из первого аргумента.</summary>
    private StatSet Entity(CallbackArguments args, string function)
    {
        double id = args.AsType(0, function, DataType.Number).Number;
        return _entities((long)id) ?? throw new ScriptRuntimeException($"unknown entity {id}");
    }

    /// <summary>Номер стата из второго аргумента.</summary>
    private int Stat(CallbackArguments args, string function)
    {
        string id = args.AsType(1, function, DataType.String).String;
        return _table.TryStatIndex(id, out int stat) ? stat : throw new ScriptRuntimeException($"unknown stat {id}");
    }
}
