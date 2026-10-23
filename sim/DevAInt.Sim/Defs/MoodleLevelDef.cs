using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Уровень мудла: порог значения стата и эффекты уровня.</summary>
public sealed record MoodleLevelDef
{
    /// <summary>Значение стата, начиная с которого действует уровень.</summary>
    public required double At { get; init; }

    /// <summary>Модификаторы, которые уровень накладывает, пока действует.</summary>
    public IReadOnlyList<EffectDef> Effects { get; init; } = [];
}
