using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Определение стата: диапазон, скорость изменения и мудлы на порогах.</summary>
public sealed record StatDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Нижняя граница значения.</summary>
    public required float Min { get; init; }

    /// <summary>Верхняя граница значения.</summary>
    public required float Max { get; init; }

    /// <summary>Изменение значения за игровую минуту.</summary>
    public float Rate { get; init; }

    /// <summary>Мудлы, которые появляются на порогах стата.</summary>
    public IReadOnlyList<MoodleDef> Moodles { get; init; } = [];
}
