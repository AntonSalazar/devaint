using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Defs;

/// <summary>Тесты смысловой проверки существ и существ карты после загрузки модов.</summary>
public class CreatureValidationTest
{
    /// <summary>Неверное существо даёт ошибку с id и текстом проблемы.</summary>
    [Theory]
    [InlineData("""{ "id": "alpha:c", "radius": 0.5 }""", "radius 0.5 must be within 0..0.5")]
    [InlineData("""{ "id": "alpha:c", "speed": 0 }""", "speed 0 must be positive")]
    [InlineData("""{ "id": "alpha:c", "stats": ["alpha:nope"] }""", "stats: unknown stat alpha:nope")]
    [InlineData("""{ "id": "alpha:c", "color": "green" }""", "color green must look like #rrggbb")]
    [InlineData("""{ "id": "alpha:c", "attack": { "range": 0, "seconds": 1, "stat": "alpha:hp", "add": -1 } }""", "attack: range 0 must be positive")]
    [InlineData("""{ "id": "alpha:c", "attack": { "range": 1, "seconds": 1, "stat": "alpha:nope", "add": -1 } }""", "attack: unknown stat alpha:nope")]
    [InlineData("""{ "id": "alpha:c", "brain": { "sight": 5, "targets": ["alpha:nobody"] } }""", "brain: unknown target creature alpha:nobody")]
    [InlineData("""{ "id": "alpha:c", "shove": { "range": 1, "seconds": 1, "push": -1, "stun": 1 } }""", "shove: push -1 must not be negative")]
    public void BadCreatureIsError(string creature, string message)
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/creatures.jsonc", $$"""
            { "stats": [ { "id": "alpha:hp", "min": 0, "max": 10, "start": 10, "deathAt": 0 } ], "creatures": [ {{creature}} ] }
            """);

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal("alpha:c", error.JsonPath);
        Assert.Equal(message, error.Message);
    }

    /// <summary>Существа карты: неизвестный вид, клетка вне карты, неверное число; неизвестный вид игрока.</summary>
    [Fact]
    public void BadMapCreaturesAreErrors()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/map.jsonc", """
            { "maps": [ { "id": "alpha:m", "width": 4, "height": 4, "spawn": { "points": [ { "x": 1, "y": 1 } ], "creature": "alpha:ghost" },
              "creatures": [ { "creature": "alpha:ghost", "x": 1, "y": 1 }, { "creature": "alpha:ghost", "x": 9, "y": 1, "count": 0 } ] } ] }
            """);

        string[] messages = [.. System.Linq.Enumerable.Select(mods.Load("alpha").Errors, error => error.Message)];

        Assert.Equal(
            [
                "spawn: unknown creature alpha:ghost",
                "creatures[0]: unknown creature alpha:ghost",
                "creatures[1]: unknown creature alpha:ghost",
                "creatures[1]: cell 9,1,0 is outside the map",
                "creatures[1]: count 0 must be positive",
            ],
            messages);
    }

    /// <summary>Неверное действие управления: пустые клавиши, неизвестная клавиша, needs не на зажатое действие.</summary>
    [Theory]
    [InlineData("""{ "id": "alpha:c", "keys": [], "does": "attack" }""", "keys must not be empty")]
    [InlineData("""{ "id": "alpha:c", "keys": ["F1"], "does": "attack" }""", "key F1 must be mouse_left, mouse_right, mouse_middle or key_ and a key name")]
    [InlineData("""{ "id": "alpha:c", "keys": ["key_f"], "needs": "alpha:c", "does": "attack" }""", "needs alpha:c must be a hold control")]
    public void BadControlIsError(string control, string message)
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/controls.jsonc", $$"""{ "controls": [ {{control}} ] }""");

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal("alpha:c", error.JsonPath);
        Assert.Equal(message, error.Message);
    }
}
