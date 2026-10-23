using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DevAInt.Sim.Defs;

/// <summary>Проверка цвета в определениях: только #rrggbb.</summary>
public static partial class DefColor
{
    /// <summary>Проблема цвета или пусто, если формат верный.</summary>
    public static IEnumerable<string> Validate(string color)
    {
        if (!ColorRegex().IsMatch(color))
        {
            yield return $"color {color} must look like #rrggbb";
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRegex();
}
