using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Defs;

/// <summary>Тесты смысловой проверки предметов и стартовых предметов карты.</summary>
public class ItemValidationTest
{
    /// <summary>Неверное применение предмета даёт ошибку с id предмета.</summary>
    [Theory]
    [InlineData("""{ "action": "alpha:nope", "seconds": 1 }""", "uses[0]: unknown action alpha:nope")]
    [InlineData("""{ "action": "alpha:eat", "seconds": 0 }""", "uses[0]: seconds 0 must be positive")]
    [InlineData("""{ "action": "alpha:eat", "seconds": 1, "stats": [ { "stat": "alpha:nope", "add": 1 } ] }""", "uses[0]: unknown stat alpha:nope")]
    [InlineData("""{ "action": "alpha:eat", "seconds": 1 }, { "action": "alpha:eat", "seconds": 2 }""", "uses[0]: action alpha:eat is used twice")]
    public void BadUseIsError(string uses, string message)
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/items.jsonc", $$"""
            { "actions": [ { "id": "alpha:eat" } ], "items": [ { "id": "alpha:item", "uses": [ {{uses}} ] } ] }
            """);

        ModLoadResult result = mods.Load("alpha");

        ModError error = result.Errors[0];
        Assert.Equal("alpha:item", error.JsonPath);
        Assert.Equal(message, error.Message);
    }

    /// <summary>Неизвестный стартовый предмет карты - ошибка.</summary>
    [Fact]
    public void UnknownSpawnItemIsError()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/map.jsonc", """
            { "maps": [ { "id": "alpha:map", "width": 4, "height": 4, "spawn": { "points": [ { "x": 1, "y": 1 } ], "items": ["alpha:ghost"] } } ] }
            """);

        Assert.Equal("spawn: unknown item alpha:ghost", Assert.Single(mods.Load("alpha").Errors).Message);
    }
}
