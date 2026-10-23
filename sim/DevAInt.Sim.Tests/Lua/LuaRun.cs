using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Lua;

/// <summary>Sim с модом core и модом alpha из одного скрипта main.lua.</summary>
public sealed record LuaRun(Simulation Sim, ListLog Log)
{
    /// <summary>Загрузка core и alpha со скриптом; папка модов удаляется сразу после загрузки.</summary>
    public static LuaRun Start(string script)
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("alpha", depends: "core").Write("alpha/scripts/main.lua", script);
        ModLoadResult result = mods.Load("core", "alpha");
        Assert.True(result.Ok, string.Join("\n", result.Errors));

        ListLog log = new();
        return new LuaRun(new Simulation(result, log), log);
    }

    /// <summary>Вывод print скрипта без префикса мода, одной строкой; ошибки загрузки - провал теста.</summary>
    public static string Print(string script)
    {
        LuaRun run = Start(script);
        Assert.Empty(run.Sim.ScriptErrors);
        Assert.Empty(run.Log.Errors);
        return string.Join("\n", run.Log.Infos).Replace("[alpha] ", string.Empty);
    }
}
