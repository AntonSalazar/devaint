using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Как строится объект или стена: материалы тратятся, инструменты нужны и остаются, секунды - длительность.</summary>
public sealed record BuildDef
{
    /// <summary>Материалы.</summary>
    public IReadOnlyList<ItemCountDef> Inputs { get; init; } = [];

    /// <summary>Инструменты.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>Длительность в секундах симуляции.</summary>
    public required double Seconds { get; init; }

    /// <summary>Проблемы постройки.</summary>
    internal IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Seconds <= 0)
        {
            yield return $"build: seconds {Seconds} must be positive";
        }

        foreach (string problem in ItemCountDef.Validate(Inputs, "build.inputs", defs))
        {
            yield return problem;
        }

        foreach (string tool in Tools)
        {
            if (!defs.TryGet<ItemDef>(tool, out _))
            {
                yield return $"build.tools: unknown item {tool}";
            }
        }
    }
}
