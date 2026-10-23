using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Мудл стата: id и пороги уровней по возрастанию.</summary>
public sealed record MoodleDef
{
    /// <summary>Id мудла.</summary>
    public required string Id { get; init; }

    /// <summary>Пороги значения стата, начиная с которых действует уровень 1, 2 и далее.</summary>
    public required IReadOnlyList<float> Levels { get; init; }
}
