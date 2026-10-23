using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Defs;

/// <summary>Тесты смысловой проверки статов после загрузки модов.</summary>
public class StatValidationTest
{
    /// <summary>Неверный стат даёт ошибку с файлом, id и текстом проблемы.</summary>
    [Theory]
    [InlineData("""{ "id": "alpha:s", "min": 10, "max": 10 }""", "min 10 must be less than max 10")]
    [InlineData("""{ "id": "alpha:s", "min": 0, "max": 100, "moodles": [ { "id": "alpha:m", "levels": [] } ] }""", "moodle alpha:m: moodle needs at least one level")]
    [InlineData("""{ "id": "alpha:s", "min": 0, "max": 100, "moodles": [ { "id": "alpha:m", "levels": [{ "at": 50 }, { "at": 50 }] } ] }""", "moodle alpha:m: level 2 threshold 50 must be greater than level 1 threshold")]
    [InlineData("""{ "id": "alpha:s", "min": 0, "max": 100, "moodles": [ { "id": "alpha:m", "levels": [{ "at": 101 }] } ] }""", "moodle alpha:m: level 1 threshold 101 is outside 0..100")]
    [InlineData("""{ "id": "alpha:s", "min": 0, "max": 100, "moodles": [ { "id": "Bad", "levels": [{ "at": 1 }] } ] }""", "moodle Bad: moodle id must look like mod:name")]
    [InlineData("""{ "id": "alpha:s", "min": 0, "max": 100, "moodles": [ { "id": "alpha:m", "levels": [{ "at": 1, "effects": [ { "stat": "alpha:nope", "kind": "add", "value": 1 } ] }] } ] }""", "moodle alpha:m: level 1 effect targets unknown stat alpha:nope")]
    [InlineData("""{ "id": "alpha:s", "min": 0, "max": 100, "moodles": [ { "id": "alpha:m", "levels": [{ "at": 1, "effects": [ { "stat": "alpha:s", "kind": "multiply", "value": -1 } ] }] } ] }""", "moodle alpha:m: level 1 effect multiplier -1 must not be negative")]
    public void BadStatIsError(string stat, string message)
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", $$"""{ "stats": [ {{stat}} ] }""");

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal("mods/alpha/data/stats.jsonc", error.File);
        Assert.Equal("alpha:s", error.JsonPath);
        Assert.StartsWith(message, error.Message);
    }

    /// <summary>Один id мудла в двух статах - ошибка у обоих.</summary>
    [Fact]
    public void MoodleIdIsUniqueAcrossStats()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", """
            { "stats": [
              { "id": "alpha:a", "min": 0, "max": 1, "moodles": [ { "id": "alpha:m", "levels": [{ "at": 1 }] } ] },
              { "id": "alpha:b", "min": 0, "max": 1, "moodles": [ { "id": "alpha:m", "levels": [{ "at": 1 }] } ] } ] }
            """);

        ModLoadResult result = mods.Load("alpha");

        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Contains("moodle id is already used by stat", error.Message));
    }

    /// <summary>Неизвестный вид эффекта - ошибка разбора со строкой.</summary>
    [Fact]
    public void UnknownEffectKindHasLine()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/stats.jsonc", """
            { "stats": [ { "id": "alpha:s", "min": 0, "max": 1, "moodles": [ { "id": "alpha:m",
              "levels": [{ "at": 1, "effects": [ { "stat": "alpha:s", "kind": "divide", "value": 2 } ] }] } ] } ] }
            """);

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal(2, error.Line);
    }
}
