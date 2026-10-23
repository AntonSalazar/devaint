using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Тесты патчей set, add и remove из второго мода.</summary>
public class PatchTest
{
    /// <summary>Исходный стат мода alpha.</summary>
    private const string BaseStats = """
        { "stats": [ { "id": "alpha:hunger", "min": 0, "max": 100, "rate": 0.05,
          "moodles": [ { "id": "alpha:hungry", "levels": [40, 70] }, { "id": "alpha:starving", "levels": [95] } ] } ] }
        """;

    /// <summary>set заменяет поле.</summary>
    [Fact]
    public void SetReplacesField()
    {
        StatDef hunger = Patched("""{ "op": "set", "target": "alpha:hunger", "path": "rate", "value": 0.02 }""");

        Assert.Equal(0.02f, hunger.Rate);
    }

    /// <summary>set находит элемент списка по id.</summary>
    [Fact]
    public void SetFindsListItemById()
    {
        StatDef hunger = Patched("""{ "op": "set", "target": "alpha:hunger", "path": "moodles.alpha:hungry.levels", "value": [30, 60] }""");

        Assert.Equal([30f, 60f], hunger.Moodles[0].Levels);
    }

    /// <summary>set находит элемент списка по номеру.</summary>
    [Fact]
    public void SetFindsListItemByIndex()
    {
        StatDef hunger = Patched("""{ "op": "set", "target": "alpha:hunger", "path": "moodles.1.levels.0", "value": 99 }""");

        Assert.Equal([99f], hunger.Moodles[1].Levels);
    }

    /// <summary>add добавляет элемент в конец списка.</summary>
    [Fact]
    public void AddAppendsToList()
    {
        StatDef hunger = Patched("""{ "op": "add", "target": "alpha:hunger", "path": "moodles", "value": { "id": "beta:sparks", "levels": [50] } }""");

        Assert.Equal(["alpha:hungry", "alpha:starving", "beta:sparks"], hunger.Moodles.Select(moodle => moodle.Id));
    }

    /// <summary>remove удаляет элемент списка по id.</summary>
    [Fact]
    public void RemoveDeletesListItem()
    {
        StatDef hunger = Patched("""{ "op": "remove", "target": "alpha:hunger", "path": "moodles.alpha:hungry" }""");

        Assert.Equal("alpha:starving", Assert.Single(hunger.Moodles).Id);
    }

    /// <summary>remove удаляет необязательное поле, оно получает значение по умолчанию.</summary>
    [Fact]
    public void RemoveDeletesField()
    {
        StatDef hunger = Patched("""{ "op": "remove", "target": "alpha:hunger", "path": "rate" }""");

        Assert.Equal(0f, hunger.Rate);
    }

    /// <summary>remove без пути удаляет определение целиком.</summary>
    [Fact]
    public void RemoveWithoutPathDeletesDef()
    {
        ModLoadResult result = Load("""{ "op": "remove", "target": "alpha:hunger" }""");

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.False(result.Defs.TryGet<StatDef>("alpha:hunger", out _));
    }

    /// <summary>Патчи применяются по порядку: второй видит результат первого.</summary>
    [Fact]
    public void PatchesApplyInOrder()
    {
        StatDef hunger = Patched(
            """{ "op": "set", "target": "alpha:hunger", "path": "rate", "value": 1 }""",
            """{ "op": "set", "target": "alpha:hunger", "path": "max", "value": 50 }""");

        Assert.Equal(1f, hunger.Rate);
        Assert.Equal(50f, hunger.Max);
    }

    /// <summary>Ошибочные патчи дают ошибку с путём патча и не меняют определение.</summary>
    [Theory]
    [InlineData("""{ "op": "set", "target": "alpha:hungerr", "path": "rate", "value": 1 }""", "target alpha:hungerr not found")]
    [InlineData("""{ "op": "set", "target": "alpha:hunger", "path": "moodles.alpha:nope.levels", "value": [1] }""", "no element alpha:nope")]
    [InlineData("""{ "op": "add", "target": "alpha:hunger", "path": "rate", "value": 1 }""", "rate is not a list")]
    [InlineData("""{ "op": "set", "target": "alpha:hunger", "path": "rate.x", "value": 1 }""", "value is neither an object nor a list")]
    [InlineData("""{ "op": "set", "target": "alpha:hunger", "path": "rate", "value": "fast" }""", "alpha:hunger is invalid after patch")]
    [InlineData("""{ "op": "set", "target": "alpha:hunger", "path": "speed", "value": 1 }""", "alpha:hunger is invalid after patch")]
    [InlineData("""{ "op": "set", "target": "alpha:hunger", "path": "id", "value": "alpha:other" }""", "patch cannot change id")]
    [InlineData("""{ "op": "set", "target": "alpha:hunger", "path": "rate" }""", "set requires 'value'")]
    [InlineData("""{ "op": "add", "target": "alpha:hunger", "value": 1 }""", "add requires 'path'")]
    public void BadPatchIsErrorAndKeepsDef(string patch, string message)
    {
        ModLoadResult result = Load(patch);

        ModError error = Assert.Single(result.Errors);
        Assert.Equal("mods/beta/data/patches.jsonc", error.File);
        Assert.Equal("$.patches[0]", error.JsonPath);
        Assert.Contains(message, error.Message);
        Assert.Equal(0.05f, result.Defs.Get<StatDef>("alpha:hunger").Rate);
    }

    /// <summary>Неизвестная операция - ошибка со строкой.</summary>
    [Fact]
    public void UnknownOpHasLine()
    {
        ModError error = Assert.Single(Load("""{ "op": "replace", "target": "alpha:hunger" }""").Errors);

        Assert.Equal(1, error.Line);
        Assert.Equal("$.patches[0].op", error.JsonPath);
    }

    /// <summary>Мод alpha со статом и мод beta с патчами.</summary>
    private static ModLoadResult Load(params string[] patches)
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", BaseStats);
        mods.Manifest("beta", depends: "alpha").Write("beta/data/patches.jsonc", $$"""{ "patches": [{{string.Join(", ", patches)}}] }""");
        return mods.Load("alpha", "beta");
    }

    /// <summary>Итоговый стат после успешных патчей.</summary>
    private static StatDef Patched(params string[] patches)
    {
        ModLoadResult result = Load(patches);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return result.Defs.Get<StatDef>("alpha:hunger");
    }
}
