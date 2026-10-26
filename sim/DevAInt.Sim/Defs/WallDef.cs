using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Вид стены на ребре клетки: сплошная, с дверным проёмом или с оконной рамой, как в Project Zomboid.</summary>
public enum WallKind : byte
{
    /// <summary>Сплошная стена.</summary>
    Wall,

    /// <summary>Стена с дверным проёмом: без двери проход открыт, дверь ставится отдельно.</summary>
    DoorFrame,

    /// <summary>Стена с оконной рамой: через неё видно, но не пройти; окно ставится отдельно.</summary>
    WindowFrame,
}

/// <summary>Определение стены на ребре клетки: сплошной, с дверным проёмом или с оконной рамой.</summary>
public sealed record WallDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Вид: сплошная стена, дверной проём или оконная рама.</summary>
    public required WallKind Kind { get; init; }

    /// <summary>Цвет в виде #rrggbb, пока моделей и текстур нет.</summary>
    public string Color { get; init; } = "#a0a0a0";

    /// <summary>Как стена строится игроком, null - не строится и не разбирается.</summary>
    public BuildDef? Build { get; init; }

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        foreach (string problem in DefColor.Validate(Color))
        {
            yield return problem;
        }

        foreach (string problem in Build?.Validate(defs) ?? [])
        {
            yield return problem;
        }
    }
}
