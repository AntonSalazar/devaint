using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Тесты чтения определений из data/*.jsonc.</summary>
public class DefLoadingTest
{
    /// <summary>Стат читается со всеми полями, комментарии и висячие запятые допустимы.</summary>
    [Fact]
    public void LoadsStat()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", """
            // комментарий
            { "stats": [ { "id": "alpha:hunger", "min": 0, "max": 100, "rate": 0.5,
                           "moodles": [ { "id": "alpha:hungry", "levels": [40, 70] }, ] } ] }
            """);

        ModLoadResult result = mods.Load("alpha");

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        StatDef hunger = result.Defs.Get<StatDef>("alpha:hunger");
        Assert.Equal(100, hunger.Max);
        Assert.Equal(0.5f, hunger.Rate);
        Assert.Equal([40f, 70f], Assert.Single(hunger.Moodles).Levels);
    }

    /// <summary>Id чужого пространства имён - ошибка.</summary>
    [Fact]
    public void ForeignNamespaceIsError()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", """{ "stats": [ { "id": "core:hunger", "min": 0, "max": 1 } ] }""");

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal("mods/alpha/data/stats.jsonc", error.File);
        Assert.Equal("$.stats[0].id", error.JsonPath);
    }

    /// <summary>Второй мод не может определить тот же id.</summary>
    [Fact]
    public void DuplicateIdIsError()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/a.jsonc", """{ "stats": [ { "id": "alpha:s", "min": 0, "max": 1 } ] }""");
        mods.Write("alpha/data/b.jsonc", """{ "stats": [ { "id": "alpha:s", "min": 0, "max": 2 } ] }""");

        ModLoadResult result = mods.Load("alpha");

        ModError error = Assert.Single(result.Errors);
        Assert.Contains("alpha:s is already defined in mods/alpha/data/a.jsonc", error.Message);
        Assert.Equal(1, result.Defs.Get<StatDef>("alpha:s").Max);
    }

    /// <summary>Неверный тип поля - ошибка со строкой и путём.</summary>
    [Fact]
    public void WrongTypeHasLineAndPath()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", "{ \"stats\": [\n  { \"id\": \"alpha:s\", \"min\": 0,\n    \"max\": \"lots\" } ] }");

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal(3, error.Line);
        Assert.Equal("$.stats[0].max", error.JsonPath);
    }

    /// <summary>Неизвестная категория и неизвестное поле - ошибки.</summary>
    [Theory]
    [InlineData("""{ "items": [] }""")]
    [InlineData("""{ "stats": [ { "id": "alpha:s", "min": 0, "max": 1, "speed": 2 } ] }""")]
    public void UnknownFieldIsError(string text)
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", text);

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal(1, error.Line);
    }

    /// <summary>Пропущенное обязательное поле - ошибка.</summary>
    [Fact]
    public void MissingRequiredFieldIsError()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", """{ "stats": [ { "id": "alpha:s", "min": 0 } ] }""");

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Contains("max", error.Message);
    }

    /// <summary>Ошибка в одном файле не мешает загрузить остальные.</summary>
    [Fact]
    public void BrokenFileDoesNotStopOthers()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/a.jsonc", "{ broken");
        mods.Write("alpha/data/b.jsonc", """{ "stats": [ { "id": "alpha:s", "min": 0, "max": 1 } ] }""");

        ModLoadResult result = mods.Load("alpha");

        Assert.Single(result.Errors);
        Assert.True(result.Defs.TryGet<StatDef>("alpha:s", out _));
    }

    /// <summary>Каждая категория DefFile.Categories есть в DefFile списком с тем же именем.</summary>
    [Fact]
    public void CategoriesMatchDefFileProperties()
    {
        string[] properties = [.. typeof(DefFile).GetProperties()
            .Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
            .Where(name => name != "patches")
            .Order()];

        Assert.Equal(DefFile.Categories.Keys.Order(), properties);
    }
}
