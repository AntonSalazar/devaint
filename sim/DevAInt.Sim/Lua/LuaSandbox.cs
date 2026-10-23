using MoonSharp.Interpreter;

namespace DevAInt.Sim.Lua;

/// <summary>Создание Lua-состояния мода: библиотеки без доступа к системе и диспетчер с лимитом инструкций.</summary>
internal static class LuaSandbox
{
    /// <summary>Лимит инструкций на один вызов хука или загрузку файла.</summary>
    public const int InstructionBudget = 1_000_000;

    /// <summary>Наибольшая длина строки из string.rep, чтобы мод не съел память одним вызовом.</summary>
    public const int MaxRepLength = 1_000_000;

    /// <summary>Модули MoonSharp: без os, io, load, require, debug и метатаблиц.</summary>
    private const CoreModules Modules = CoreModules.Preset_HardSandbox | CoreModules.Coroutine | CoreModules.ErrorHandling;

    // Диспетчер вызывает функции внутри одной долгоживущей корутины: ограничение инструкций работает
    // только в корутине, а новая корутина на каждый вызов стоит около 200 мкс против 2 мкс.
    // C#-функции библиотеки, которые вызывают Lua изнутри, нельзя прервать по лимиту, поэтому
    // table.sort, string.gsub с функцией или таблицей и xpcall заменены версиями на чистом Lua.
    private const string Prelude = """
        local yield = coroutine.yield
        local find, sub, native_gsub, native_rep = string.find, string.sub, string.gsub, string.rep
        local type, tostring, error, select, pcall, floor = type, tostring, error, select, pcall, math.floor
        local concat, unpack = table.concat, table.unpack
        local max_rep = __MAX_REP

        local function pack(...)
          return { n = select('#', ...), ... }
        end

        table.sort = function(t, less)
          less = less or function(a, b) return a < b end
          local function insertion(lo, hi)
            for i = lo + 1, hi do
              local v, j = t[i], i - 1
              while j >= lo and less(v, t[j]) do
                t[j + 1] = t[j]
                j = j - 1
              end
              t[j + 1] = v
            end
          end
          local function quick(lo, hi)
            while hi - lo > 12 do
              local p = t[floor((lo + hi) / 2)]
              local i, j = lo, hi
              while i <= j do
                while less(t[i], p) do i = i + 1 end
                while less(p, t[j]) do j = j - 1 end
                if i <= j then
                  t[i], t[j] = t[j], t[i]
                  i, j = i + 1, j - 1
                end
              end
              if j - lo < hi - i then
                quick(lo, j)
                lo = i
              else
                quick(i, hi)
                hi = j
              end
            end
            insertion(lo, hi)
          end
          quick(1, #t)
        end

        string.gsub = function(s, pattern, repl, n)
          local kind = type(repl)
          if kind ~= 'function' and kind ~= 'table' then
            return native_gsub(s, pattern, repl, n)
          end
          s = tostring(s)
          local anchored = sub(pattern, 1, 1) == '^'
          local out, count, pos, len = {}, 0, 1, #s
          while (n == nil or count < n) and pos <= len + 1 do
            local found = pack(find(s, pattern, pos))
            local st, en = found[1], found[2]
            if st == nil then break end
            out[#out + 1] = sub(s, pos, st - 1)
            local whole = sub(s, st, en)
            local value
            if kind == 'function' then
              if found.n > 2 then value = repl(unpack(found, 3, found.n)) else value = repl(whole) end
            else
              if found.n > 2 then value = repl[found[3]] else value = repl[whole] end
            end
            if value == nil or value == false then
              out[#out + 1] = whole
            elseif type(value) == 'string' or type(value) == 'number' then
              out[#out + 1] = tostring(value)
            else
              error('invalid replacement value (a ' .. type(value) .. ')', 2)
            end
            count = count + 1
            if en >= st then
              pos = en + 1
            else
              if st <= len then out[#out + 1] = sub(s, st, st) end
              pos = st + 1
            end
            if anchored then break end
          end
          out[#out + 1] = sub(s, pos)
          return concat(out), count
        end

        string.rep = function(s, count, sep)
          local total = #tostring(s) * count + (sep and #tostring(sep) * (count - 1) or 0)
          if total > max_rep then
            error('string.rep result is longer than ' .. max_rep .. ' characters', 2)
          end
          return native_rep(s, count, sep)
        end

        xpcall = function(f, handler, ...)
          local result = pack(pcall(f, ...))
          if result[1] then
            return unpack(result, 1, result.n)
          end
          return false, handler(result[2])
        end

        return function(fn, a, b, c, d)
          while true do
            fn, a, b, c, d = yield(fn(a, b, c, d))
          end
        end
        """;

    /// <summary>Глобальные имена, которые убираются после подготовки.</summary>
    private static readonly string[] _removedGlobals = ["coroutine", "collectgarbage", "package", "_MOONSHARP", "__MAX_REP"];

    /// <summary>Новое Lua-состояние и функция-диспетчер для него.</summary>
    public static (Script Script, Closure Dispatcher) Create()
    {
        Script script = new(Modules);
        script.Globals["__MAX_REP"] = MaxRepLength;
        Closure dispatcher = script.DoString(Prelude, null, "sandbox").Function;

        // Прелюдия уже взяла нужное в свои локальные переменные, модам эти глобалы не нужны
        foreach (string name in _removedGlobals)
        {
            script.Globals[name] = DynValue.Nil;
        }

        script.Globals.Get("string").Table["dump"] = DynValue.Nil;
        return (script, dispatcher);
    }
}
