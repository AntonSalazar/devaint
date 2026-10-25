using System;
using System.Collections.Generic;
using System.IO;
using DevAInt.Sim.Grid;
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

    /// <summary>Имя хука действия управления с поведением lua: сущность, id действия, точка курсора.</summary>
    public const string ControlHook = "control";

    /// <summary>Имя хука выбора точки появления: id игрока и точки, ответ - номер точки с единицы.</summary>
    public const string ChooseSpawnHook = "choose_spawn";

    /// <summary>Известные хуки.</summary>
    private static readonly HashSet<string> _hooks = [TickHook, StatThresholdHook, ControlHook, ChooseSpawnHook];

    /// <summary>Таблица статов для перевода id в номера.</summary>
    private readonly StatTable _table;

    /// <summary>Статы сущности по id, null если её нет.</summary>
    private readonly Func<long, StatSet?> _entities;

    /// <summary>Все сущности мира, для запросов к миру.</summary>
    private readonly Func<IReadOnlyList<Entity>> _all;

    /// <summary>Лог для ошибок и print.</summary>
    private readonly ISimLog _log;

    /// <summary>Lua-моды в порядке загрузки.</summary>
    private readonly List<LuaMod> _mods = [];

    /// <summary>Id мудлов как Lua-строки, чтобы не создавать их на каждое событие.</summary>
    private readonly DynValue[] _moodleIds;

    /// <summary>Хост с доступом к статам сущностей и списку всех сущностей мира.</summary>
    public LuaHost(StatTable table, Func<long, StatSet?> entities, Func<IReadOnlyList<Entity>> all, ISimLog log)
    {
        _all = all;
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

    /// <summary>Хук control у всех модов: каждый мод сам смотрит, его ли это действие.</summary>
    public void RaiseControl(long entity, string control, double x, double y)
    {
        DynValue id = DynValue.NewNumber(entity);
        DynValue name = DynValue.NewString(control);
        DynValue aimX = DynValue.NewNumber(x);
        DynValue aimY = DynValue.NewNumber(y);
        foreach (LuaMod mod in _mods)
        {
            mod.Raise(ControlHook, id, name, aimX, aimY);
        }
    }

    /// <summary>Номер точки появления от хука choose_spawn, null - ни один мод не ответил; поздний мод спрашивается первым.</summary>
    public int? ChooseSpawn(string player, IReadOnlyList<CellPos> points)
    {
        for (int m = _mods.Count - 1; m >= 0; m--)
        {
            LuaMod mod = _mods[m];
            if (!mod.Handles(ChooseSpawnHook))
            {
                continue;
            }

            Table list = new(mod.Script);
            foreach (CellPos point in points)
            {
                Table cell = new(mod.Script);
                cell["x"] = point.X;
                cell["y"] = point.Y;
                cell["z"] = point.Z;
                list.Append(DynValue.NewTable(cell));
            }

            DynValue answer = mod.Ask(ChooseSpawnHook, DynValue.NewString(player), DynValue.NewTable(list), DynValue.Nil, DynValue.Nil);
            if (answer.IsNil())
            {
                continue;
            }

            if (answer.Type == DataType.Number && answer.Number == Math.Floor(answer.Number) && answer.Number >= 1 && answer.Number <= points.Count)
            {
                return (int)answer.Number - 1;
            }

            _log.Error($"mod {mod.Id}: hook {ChooseSpawnHook}: answer {answer} is not a point number 1..{points.Count}");
        }

        return null;
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

        Table entities = new(mod.Script);
        entities["near"] = DynValue.NewCallback((context, args) => EntitiesNear(mod, args), "entities.near");
        mod.Script.Globals["entities"] = entities;
    }

    /// <summary>entities.near: сущности этажа z в радиусе от точки x, y - таблицы id, creature, x, y.</summary>
    private DynValue EntitiesNear(LuaMod mod, CallbackArguments args)
    {
        double x = args.AsType(0, "entities.near", DataType.Number).Number;
        double y = args.AsType(1, "entities.near", DataType.Number).Number;
        int z = (int)args.AsType(2, "entities.near", DataType.Number).Number;
        double radius = args.AsType(3, "entities.near", DataType.Number).Number;
        Table result = new(mod.Script);
        foreach (Entity entity in _all())
        {
            double dx = entity.Position.X - x;
            double dy = entity.Position.Y - y;
            if (entity.Position.Z != z || (dx * dx) + (dy * dy) > radius * radius)
            {
                continue;
            }

            Table row = new(mod.Script);
            row["id"] = entity.Id;
            row["creature"] = entity.Creature is null ? DynValue.Nil : DynValue.NewString(entity.Creature.Id);
            row["x"] = entity.Position.X;
            row["y"] = entity.Position.Y;
            result.Append(DynValue.NewTable(row));
        }

        return DynValue.NewTable(result);
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
