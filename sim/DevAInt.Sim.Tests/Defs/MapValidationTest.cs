using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Defs;

/// <summary>Тесты смысловой проверки карт после загрузки модов.</summary>
public class MapValidationTest
{
    /// <summary>Полы, стены и двери мода alpha для проверок.</summary>
    private const string World = """
        { "floors": [ { "id": "alpha:floor" } ],
          "walls": [ { "id": "alpha:wall", "kind": "wall" }, { "id": "alpha:door", "kind": "door" } ] }
        """;

    /// <summary>Верная карта загружается без ошибок.</summary>
    [Fact]
    public void ValidMapLoads()
    {
        ModLoadResult result = Load("""
            { "floor": "alpha:floor", "x": 0, "y": 0, "w": 4, "h": 4 },
            { "walls": "alpha:wall", "x": 0, "y": 0, "w": 4, "h": 4, "z": 1 },
            { "door": "alpha:door", "x": 0, "y": 0, "side": "west", "open": true }
            """);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
    }

    /// <summary>Неверная операция даёт ошибку с номером операции.</summary>
    [Theory]
    [InlineData("""{ "x": 0, "y": 0 }""", "build[0]: needs exactly one of floor, walls, door")]
    [InlineData("""{ "floor": "alpha:floor", "walls": "alpha:wall", "x": 0, "y": 0 }""", "build[0]: needs exactly one of floor, walls, door")]
    [InlineData("""{ "floor": "alpha:floor", "x": 6, "y": 0, "w": 3 }""", "build[0]: rect 6,0 3x1 is outside the map 8x8")]
    [InlineData("""{ "floor": "alpha:floor", "x": 0, "y": 0, "z": 9 }""", "build[0]: z 9 must be within -2..8")]
    [InlineData("""{ "floor": "alpha:nope", "x": 0, "y": 0 }""", "build[0]: unknown floor alpha:nope")]
    [InlineData("""{ "walls": "alpha:door", "x": 0, "y": 0 }""", "build[0]: unknown wall alpha:door")]
    [InlineData("""{ "door": "alpha:wall", "x": 0, "y": 0, "side": "north" }""", "build[0]: unknown door alpha:wall")]
    [InlineData("""{ "door": "alpha:door", "x": 0, "y": 0 }""", "build[0]: side is required for door and only for door")]
    [InlineData("""{ "floor": "alpha:floor", "x": 0, "y": 0, "open": true }""", "build[0]: open is only for door")]
    public void BadOpIsError(string op, string message)
    {
        ModError error = Assert.Single(Load(op).Errors);

        Assert.Equal("alpha:map", error.JsonPath);
        Assert.Equal(message, error.Message);
    }

    /// <summary>Слишком большая карта - ошибка.</summary>
    [Fact]
    public void TooLargeMapIsError()
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/map.jsonc", """{ "maps": [ { "id": "alpha:map", "width": 5000, "height": 8 } ] }""");

        Assert.Equal("size 5000x8 must be within 1..4096", Assert.Single(mods.Load("alpha").Errors).Message);
    }

    /// <summary>Мод alpha с картой 8 на 8 и операциями build.</summary>
    private static ModLoadResult Load(string ops)
    {
        using TempMods mods = new();
        mods.Manifest("alpha").Write("alpha/data/world.jsonc", World);
        mods.Write("alpha/data/map.jsonc", $$"""{ "maps": [ { "id": "alpha:map", "width": 8, "height": 8, "build": [ {{ops}} ] } ] }""");
        return mods.Load("alpha");
    }
}
