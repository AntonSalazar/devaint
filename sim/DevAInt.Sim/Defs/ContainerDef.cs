using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Вместимость инвентаря существа или контейнера объекта в килограммах.</summary>
public sealed record ContainerDef
{
    /// <summary>Наибольший общий вес предметов в килограммах.</summary>
    public required double Capacity { get; init; }

    /// <summary>Проблемы вместимости.</summary>
    internal IEnumerable<string> Validate(string where)
    {
        if (Capacity <= 0)
        {
            yield return $"{where}: capacity {Capacity} must be positive";
        }
    }
}
