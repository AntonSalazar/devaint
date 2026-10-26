using System.Linq;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using DevAInt.Sim.Ui;
using Xunit;

namespace DevAInt.Sim.Tests.Defs;

/// <summary>Тесты смысловой проверки панелей интерфейса после загрузки модов.</summary>
public class UiValidationTest
{
    /// <summary>Неверная строка панели даёт ошибку с номером строки и текстом проблемы.</summary>
    [Theory]
    [InlineData("""{ "stat": "core:hunger", "moodles": true }""", "rows[0]: needs exactly one of stat, moodles, items, text, button, separator")]
    [InlineData("""{ }""", "rows[0]: needs exactly one of stat, moodles, items, text, button, separator")]
    [InlineData("""{ "stat": "alpha:nope" }""", "rows[0]: unknown stat alpha:nope")]
    [InlineData("""{ "button": "alpha:press" }""", "rows[0]: action is required for button and only for button")]
    [InlineData("""{ "text": "alpha:hint", "action": "alpha:press" }""", "rows[0]: action is required for button and only for button")]
    public void BadRowIsError(string row, string message)
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("alpha", depends: "core").Write("alpha/data/ui.jsonc", $$"""{ "ui": [ { "id": "alpha:panel", "rows": [ {{row}} ] } ] }""");

        ModError error = Assert.Single(mods.Load("core", "alpha").Errors);

        Assert.Equal("mods/alpha/data/ui.jsonc", error.File);
        Assert.Equal("alpha:panel", error.JsonPath);
        Assert.StartsWith(message, error.Message);
    }

    /// <summary>Панель core и радар zombies проходят проверку, одинаковые действия кнопок получают один номер.</summary>
    [Fact]
    public void RepoPanelsLoad()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core", "zombies"]);
        UiTable table = new(result.Defs);

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(["core:status", "zombies:radar"], table.Panels.Select(panel => panel.Id));
        Assert.Equal(1, table.ActionCount);
        Assert.True(table.TryActionIndex("zombies:radar_range", out int index));
        Assert.Equal("zombies:radar_range", table.Action(index));
    }
}
