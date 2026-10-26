using System.Collections.Generic;
using System.Text;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Ui;

/// <summary>Правила панелей по значениям от Lua: видна ли панель и текст с подставленными значениями.</summary>
public static class UiPanels
{
    /// <summary>Ключ значения, которым ui.show прячет и показывает панель id.</summary>
    public static string ShowKey(string panel) => "show:" + panel;

    /// <summary>Видна ли панель: ui.show решает первым, без него - VisibleWhen, без обоих - видна.</summary>
    public static bool Visible(UiPanelDef panel, IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue(ShowKey(panel.Id), out string? shown))
        {
            return IsTrue(shown);
        }

        return panel.VisibleWhen is not string key || (values.TryGetValue(key, out string? value) && IsTrue(value));
    }

    /// <summary>Текст, где каждый {ключ} заменён значением от Lua; значения ещё нет - пусто.</summary>
    public static string Format(string text, IReadOnlyDictionary<string, string> values)
    {
        // Без фигурных скобок текст возвращается как есть, без новой строки
        if (!text.Contains('{'))
        {
            return text;
        }

        StringBuilder result = new(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            int open = text.IndexOf('{', at);
            int close = open < 0 ? -1 : text.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(text, at, text.Length - at);
                break;
            }

            result.Append(text, at, open - at);
            string key = text[(open + 1)..close];
            result.Append(values.TryGetValue(key, out string? value) ? value : string.Empty);
            at = close + 1;
        }

        return result.ToString();
    }

    /// <summary>Значение считается включённым: не пусто, не 0 и не false.</summary>
    private static bool IsTrue(string value) => value is not ("" or "0" or "false");
}
