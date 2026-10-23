using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;
using MoonSharp.Interpreter;

namespace DevAInt.Sim.Lua;

/// <summary>Lua-состояние одного мода: его скрипты и обработчики хуков.</summary>
internal sealed partial class LuaMod
{
    /// <summary>Лог для ошибок и print.</summary>
    private readonly ISimLog _log;

    /// <summary>Диспетчер, который вызывает функции мода с лимитом инструкций.</summary>
    private readonly Closure _dispatcher;

    /// <summary>Обработчики по имени хука.</summary>
    private readonly Dictionary<string, List<DynValue>> _handlers = [];

    /// <summary>Корутина диспетчера, пересоздаётся после ошибки или превышения лимита.</summary>
    private DynValue _coroutine;

    /// <summary>Новое Lua-состояние мода с print в лог.</summary>
    public LuaMod(string modId, ISimLog log)
    {
        Id = modId;
        _log = log;
        (Script, _dispatcher) = LuaSandbox.Create();
        Script.Options.DebugPrint = text => _log.Info($"[{Id}] {text}");
        _coroutine = NewCoroutine();
    }

    /// <summary>Id мода.</summary>
    public string Id { get; }

    /// <summary>Lua-состояние мода, в него LuaHost добавляет API.</summary>
    public Script Script { get; }

    /// <summary>Выполнение всех scripts/*.lua мода по имени файла; ошибки попадают в errors.</summary>
    public void LoadScripts(ModManifest mod, List<ModError> errors)
    {
        string dir = Path.Combine(mod.Directory, "scripts");
        string shownDir = Path.Combine(Path.GetDirectoryName(mod.File)!, "scripts");
        string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.lua") : [];
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            string shown = Path.Combine(shownDir, Path.GetFileName(file));
            DynValue chunk;
            try
            {
                chunk = Script.LoadString(File.ReadAllText(file), null, shown);
            }
            catch (SyntaxErrorException error)
            {
                errors.Add(ToError(shown, error.DecoratedMessage ?? error.Message));
                continue;
            }

            if (Run(chunk, DynValue.Nil, DynValue.Nil, DynValue.Nil, DynValue.Nil, out bool runaway) is string problem)
            {
                errors.Add(ToError(shown, runaway ? $"file exceeded {LuaSandbox.InstructionBudget} instructions" : problem));
            }
        }
    }

    /// <summary>Регистрация обработчика хука.</summary>
    public void AddHandler(string hook, DynValue handler)
    {
        if (!_handlers.TryGetValue(hook, out List<DynValue>? list))
        {
            list = [];
            _handlers[hook] = list;
        }

        list.Add(handler);
    }

    /// <summary>Вызов всех обработчиков хука; обработчик, превысивший лимит, отключается.</summary>
    public void Raise(string hook, DynValue a, DynValue b, DynValue c, DynValue d)
    {
        if (!_handlers.TryGetValue(hook, out List<DynValue>? list))
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (Run(list[i], a, b, c, d, out bool runaway) is not string problem)
            {
                continue;
            }

            if (!runaway)
            {
                _log.Error($"mod {Id}: hook {hook}: {problem}");
                continue;
            }

            _log.Error($"mod {Id}: hook {hook}: exceeded {LuaSandbox.InstructionBudget} instructions, handler disabled");
            list.RemoveAt(i);
            i--;
        }
    }

    /// <summary>Позиция в сообщении MoonSharp, первая группа - строка.</summary>
    [GeneratedRegex(@":\((\d+),[^)]*\)")]
    private static partial Regex LineRegex();

    /// <summary>Ошибка загрузки со строкой, взятой из позиции в сообщении MoonSharp.</summary>
    private static ModError ToError(string file, string message)
    {
        Match match = LineRegex().Match(message);
        int? line = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        string text = message.StartsWith(file, StringComparison.Ordinal) && match.Success ? message[(match.Index + match.Length)..].TrimStart(' ', ':') : message;
        return new ModError(file, line, string.Empty, text);
    }

    /// <summary>Вызов функции через диспетчер, текст ошибки или null; runaway - превышен лимит.</summary>
    private string? Run(DynValue function, DynValue a, DynValue b, DynValue c, DynValue d, out bool runaway)
    {
        runaway = false;
        try
        {
            _coroutine.Coroutine.Resume(function, a, b, c, d);
        }
        catch (InterpreterException error)
        {
            // После ошибки корутина мертва, следующему вызову нужна новая
            _coroutine = NewCoroutine();
            return error.DecoratedMessage ?? error.Message;
        }

        if (_coroutine.Coroutine.State != CoroutineState.ForceSuspended)
        {
            return null;
        }

        runaway = true;
        _coroutine = NewCoroutine();
        return "instruction budget exceeded";
    }

    /// <summary>Новая корутина диспетчера с лимитом инструкций.</summary>
    private DynValue NewCoroutine()
    {
        DynValue coroutine = Script.CreateCoroutine(DynValue.NewClosure(_dispatcher));
        coroutine.Coroutine.AutoYieldCounter = LuaSandbox.InstructionBudget;
        return coroutine;
    }
}
