using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Тесты настоящего мода core из папки mods репозитория.</summary>
public class CoreModTest
{
    /// <summary>core загружается без ошибок, голод на месте.</summary>
    [Fact]
    public void CoreLoads()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        StatDef hunger = result.Defs.Get<StatDef>("core:hunger");
        Assert.Equal(0.05, hunger.Rate);
        Assert.Equal([40.0, 70.0, 90.0], Assert.Single(hunger.Moodles).Levels.Select(level => level.At));
    }

    /// <summary>Итог этапа 1: core и тестовый мод с патчем дают правильные итоговые определения.</summary>
    [Fact]
    public void CoreWithPatchingMod()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("robots", depends: "core >= 0.1").Write("robots/data/robots.jsonc", """
            {
              "stats": [ { "id": "robots:heat", "min": 0, "max": 100, "rate": 0.1 } ],
              "patches": [
                { "op": "set", "target": "core:hunger", "path": "rate", "value": 0.01 },
                { "op": "add", "target": "core:hunger", "path": "moodles", "value": { "id": "robots:low_power", "levels": [{ "at": 80 }] } },
              ],
            }
            """);

        ModLoadResult result = mods.Load("robots", "core");

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(["core", "robots"], result.Mods.Select(mod => mod.Id));
        StatDef hunger = result.Defs.Get<StatDef>("core:hunger");
        Assert.Equal(0.01, hunger.Rate);
        Assert.Equal(["core:hungry", "robots:low_power"], hunger.Moodles.Select(moodle => moodle.Id));
        Assert.Equal(["core:fatigue", "core:hunger", "core:thirst", "robots:heat"], result.Defs.All<StatDef>().Select(stat => stat.Id));
    }
}
