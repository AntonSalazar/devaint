using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Мудл стата: id и уровни по возрастанию порога.</summary>
public sealed record MoodleDef
{
    /// <summary>Id мудла, уникальный во всех статах.</summary>
    public required string Id { get; init; }

    /// <summary>Уровни 1, 2 и далее.</summary>
    public required IReadOnlyList<MoodleLevelDef> Levels { get; init; }
}
