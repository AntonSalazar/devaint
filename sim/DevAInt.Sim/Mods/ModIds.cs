using System.Text.RegularExpressions;

namespace DevAInt.Sim.Mods;

/// <summary>Правила id модов и определений.</summary>
public static partial class ModIds
{
    /// <summary>Id мода: строчные латинские буквы, цифры и подчёркивание.</summary>
    public static bool IsModId(string id) => ModIdRegex().IsMatch(id);

    /// <summary>Id определения: mod:name из тех же символов.</summary>
    public static bool IsDefId(string id) => DefIdRegex().IsMatch(id);

    /// <summary>Принадлежит ли id определения моду.</summary>
    public static bool BelongsTo(string defId, string modId) => defId.StartsWith(modId + ":", System.StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex ModIdRegex();

    [GeneratedRegex("^[a-z0-9_]+:[a-z0-9_]+$")]
    private static partial Regex DefIdRegex();
}
