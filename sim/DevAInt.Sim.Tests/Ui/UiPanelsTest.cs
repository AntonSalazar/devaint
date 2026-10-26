using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Ui;
using Xunit;

namespace DevAInt.Sim.Tests.Ui;

/// <summary>Тесты правил панелей: видимость по значениям и подстановка значений в текст.</summary>
public class UiPanelsTest
{
    /// <summary>Панель без условий.</summary>
    private static readonly UiPanelDef _plain = new() { Id = "alpha:plain" };

    /// <summary>Панель, видная только при значении alpha:on.</summary>
    private static readonly UiPanelDef _conditional = new() { Id = "alpha:cond", VisibleWhen = "alpha:on" };

    /// <summary>Без значений панель видна, с условием - нет; пустое, 0 и false значат выключено.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    public void VisibleWhenFollowsValue(string? value, bool visible)
    {
        Dictionary<string, string> values = [];
        if (value is not null)
        {
            values["alpha:on"] = value;
        }

        Assert.True(UiPanels.Visible(_plain, values));
        Assert.Equal(visible, UiPanels.Visible(_conditional, values));
    }

    /// <summary>ui.show решает раньше условия: прячет обычную панель и показывает панель с невыполненным условием.</summary>
    [Fact]
    public void ShowValueWins()
    {
        Dictionary<string, string> values = new()
        {
            [UiPanels.ShowKey("alpha:plain")] = "false",
            [UiPanels.ShowKey("alpha:cond")] = "true",
        };

        Assert.False(UiPanels.Visible(_plain, values));
        Assert.True(UiPanels.Visible(_conditional, values));
    }

    /// <summary>Каждый {ключ} заменяется значением, неизвестный - пустотой, незакрытая скобка остаётся как есть.</summary>
    [Theory]
    [InlineData("Zombies: {a:n}", "Zombies: 3")]
    [InlineData("{a:n}/{a:max} {a:n}", "3/10 3")]
    [InlineData("Unknown: {a:none}.", "Unknown: .")]
    [InlineData("Open {a:n", "Open {a:n")]
    [InlineData("No keys", "No keys")]
    public void FormatSubstitutesValues(string text, string expected)
    {
        Dictionary<string, string> values = new() { ["a:n"] = "3", ["a:max"] = "10" };

        Assert.Equal(expected, UiPanels.Format(text, values));
    }
}
