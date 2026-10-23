using System;
using System.Threading.Tasks;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Lua;

/// <summary>Тесты Lua-модов: хуки, API статов, ошибки и загрузка скриптов.</summary>
public class LuaTest
{
    /// <summary>Итог этапа 3: хук мода срабатывает на пороге стата и может менять статы.</summary>
    [Fact]
    public void ThresholdHookFires()
    {
        LuaRun run = LuaRun.Start("""
            hooks.add("stat_threshold", function(entity, moodle, old_level, new_level)
              print(moodle .. " " .. old_level .. "->" .. new_level)
              stats.set(entity, "core:thirst", 50)
            end)
            """);
        long entity = run.Sim.CreateEntity();
        run.Sim.EntityStats(entity).SetValue(run.Sim.StatTable.StatIndex("core:hunger"), 45);

        run.Sim.Tick();

        Assert.Equal(["[alpha] core:hungry 0->1"], run.Log.Infos);
        Assert.Equal(50, run.Sim.EntityStats(entity).Value(run.Sim.StatTable.StatIndex("core:thirst")));
    }

    /// <summary>Хук tick вызывается каждый тик, stats.get и stats.moodle читают сущность.</summary>
    [Fact]
    public void TickHookReadsStats()
    {
        LuaRun run = LuaRun.Start("""
            local count = 0
            hooks.add("tick", function()
              count = count + 1
              print(count .. " " .. stats.moodle(1, "core:hungry") .. " " .. stats.get(1, "core:hunger"))
            end)
            """);
        run.Sim.CreateEntity();
        run.Sim.EntityStats(1).SetValue(run.Sim.StatTable.StatIndex("core:hunger"), 75);

        run.Sim.Tick();
        run.Sim.Tick();

        Assert.Equal(2, run.Log.Infos.Count);
        Assert.StartsWith("[alpha] 2 2 75.", run.Log.Infos[1]);
    }

    /// <summary>Итог этапа 3: у мода нет доступа к файлам, системе, загрузке кода и отладке.</summary>
    [Fact]
    public void ModCannotReachSystem()
    {
        LuaRun run = LuaRun.Start("""
            print(tostring(io == nil and os == nil and load == nil and loadstring == nil and dofile == nil
              and require == nil and debug == nil and coroutine == nil and collectgarbage == nil
              and package == nil and setmetatable == nil and getmetatable == nil and string.dump == nil))
            """);

        Assert.Empty(run.Sim.ScriptErrors);
        Assert.Equal(["[alpha] true"], run.Log.Infos);
    }

    /// <summary>Попытка открыть файл - ошибка загрузки скрипта, а не доступ.</summary>
    [Fact]
    public void OpeningFileFails()
    {
        LuaRun run = LuaRun.Start("""io.open("/etc/passwd")""");

        ModError error = Assert.Single(run.Sim.ScriptErrors);
        Assert.Equal("mods/alpha/scripts/main.lua", error.File);
        Assert.Contains("nil", error.Message);
    }

    /// <summary>Бесконечный цикл останавливается, обработчик отключается, остальные работают.</summary>
    [Fact]
    public void RunawayHandlerIsDisabled()
    {
        LuaRun run = LuaRun.Start("""
            hooks.add("tick", function() while true do end end)
            hooks.add("tick", function() print("alive") end)
            """);

        run.Sim.Tick();
        run.Sim.Tick();

        string error = Assert.Single(run.Log.Errors);
        Assert.Equal("mod alpha: hook tick: exceeded 1000000 instructions, handler disabled", error);
        Assert.Equal(["[alpha] alive", "[alpha] alive"], run.Log.Infos);
    }

    /// <summary>Циклы внутри обратных вызовов библиотеки тоже останавливаются лимитом.</summary>
    [Theory]
    [InlineData("""local t = {3, 1, 2} table.sort(t, function(a, b) while true do end end)""")]
    [InlineData("""string.gsub("abc", ".", function(c) while true do end end)""")]
    [InlineData("""xpcall(function() error("x") end, function() while true do end end)""")]
    [InlineData("""pcall(function() while true do end end)""")]
    [InlineData("""for w in string.gmatch("a b", "%a") do while true do end end""")]
    public async Task RunawayInsideCallbacksIsStopped(string body)
    {
        LuaRun run = LuaRun.Start($$"""hooks.add("tick", function() {{body}} end)""");

        // Если защита сломается, тик зависнет навсегда: ждём с таймаутом, чтобы тест упал, а не повесил прогон
        Task tick = Task.Run(run.Sim.Tick);
        Task first = await Task.WhenAny(tick, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.True(first == tick, "tick did not finish, the instruction budget was bypassed");
        Assert.Contains("exceeded 1000000 instructions", Assert.Single(run.Log.Errors));
    }

    /// <summary>Ошибка Lua пишется в лог с файлом и строкой, sim и обработчик продолжают работу.</summary>
    [Fact]
    public void LuaErrorIsLoggedAndHandlerStays()
    {
        LuaRun run = LuaRun.Start("""
            hooks.add("tick", function()
              error("boom")
            end)
            """);

        run.Sim.Tick();
        run.Sim.Tick();

        Assert.Equal(2, run.Log.Errors.Count);
        Assert.StartsWith("mod alpha: hook tick: mods/alpha/scripts/main.lua:(2,", run.Log.Errors[0]);
        Assert.EndsWith("boom", run.Log.Errors[0]);
    }

    /// <summary>Неверный id стата и сущности - понятные ошибки.</summary>
    [Theory]
    [InlineData("""stats.get(1, "core:nope")""", "unknown stat core:nope")]
    [InlineData("""stats.get(99, "core:hunger")""", "unknown entity 99")]
    [InlineData("""stats.moodle(1, "core:nope")""", "unknown moodle core:nope")]
    [InlineData("""stats.set(1, "core:hunger", "x")""", "bad argument #3 to 'stats.set'")]
    public void BadApiCallIsClearError(string call, string message)
    {
        LuaRun run = LuaRun.Start($$"""hooks.add("tick", function() {{call}} end)""");
        run.Sim.CreateEntity();

        run.Sim.Tick();

        Assert.Contains(message, Assert.Single(run.Log.Errors));
    }

    /// <summary>Неизвестный хук - ошибка загрузки со строкой.</summary>
    [Fact]
    public void UnknownHookIsLoadError()
    {
        LuaRun run = LuaRun.Start("""

            hooks.add("on_fire", function() end)
            """);

        ModError error = Assert.Single(run.Sim.ScriptErrors);
        Assert.Equal(2, error.Line);
        Assert.Contains("unknown hook on_fire", error.Message);
    }

    /// <summary>Синтаксическая ошибка указывает файл и строку.</summary>
    [Fact]
    public void SyntaxErrorHasFileAndLine()
    {
        LuaRun run = LuaRun.Start("local a = 1\nlocal b = = 2\n");

        ModError error = Assert.Single(run.Sim.ScriptErrors);
        Assert.Equal("mods/alpha/scripts/main.lua", error.File);
        Assert.Equal(2, error.Line);
    }

    /// <summary>Бесконечный цикл при загрузке файла - ошибка загрузки.</summary>
    [Fact]
    public void RunawayAtLoadIsError()
    {
        LuaRun run = LuaRun.Start("while true do end");

        Assert.Equal("mods/alpha/scripts/main.lua: file exceeded 1000000 instructions", Assert.Single(run.Sim.ScriptErrors).ToString());
    }

    /// <summary>Моды не видят глобальные переменные друг друга.</summary>
    [Fact]
    public void ModsDoNotShareGlobals()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("alpha", depends: "core").Write("alpha/scripts/main.lua", "shared = 1");
        mods.Manifest("beta", depends: "alpha").Write("beta/scripts/main.lua", "print(tostring(shared))");
        ListLog log = new();

        Simulation sim = new(mods.Load("core", "alpha", "beta"), log);

        Assert.Empty(sim.ScriptErrors);
        Assert.Equal(["[beta] nil"], log.Infos);
    }

    /// <summary>Файлы мода выполняются по имени: b.lua видит функцию из a.lua.</summary>
    [Fact]
    public void FilesLoadInNameOrder()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("alpha", depends: "core").Write("alpha/scripts/b.lua", "greet()").Write("alpha/scripts/a.lua", "function greet() print('hi') end");
        ListLog log = new();

        Simulation sim = new(mods.Load("core", "alpha"), log);

        Assert.Empty(sim.ScriptErrors);
        Assert.Equal(["[alpha] hi"], log.Infos);
    }
}
