using System.Collections.Generic;
using System.Linq;

namespace DevAInt.Sim.Defs;

/// <summary>Определение предмета: цвет для вида и применения предмета действиями.</summary>
public sealed record ItemDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Цвет в виде #rrggbb, пока моделей и иконок нет.</summary>
    public string Color { get; init; } = "#808080";

    /// <summary>Вес в килограммах.</summary>
    public double Weight { get; init; } = 0.1;

    /// <summary>Чем предмет можно применить: действие и его параметры для этого предмета.</summary>
    public IReadOnlyList<ItemUseDef> Uses { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        foreach (string problem in DefColor.Validate(Color))
        {
            yield return problem;
        }

        if (Weight < 0)
        {
            yield return $"weight {Weight} must not be negative";
        }

        for (int i = 0; i < Uses.Count; i++)
        {
            ItemUseDef use = Uses[i];
            if (!defs.TryGet<ActionDef>(use.Action, out _))
            {
                yield return $"uses[{i}]: unknown action {use.Action}";
            }

            if (Uses.Count(other => other.Action == use.Action) > 1)
            {
                yield return $"uses[{i}]: action {use.Action} is used twice";
            }

            if (use.Seconds <= 0)
            {
                yield return $"uses[{i}]: seconds {use.Seconds} must be positive";
            }

            foreach (StatChangeDef change in use.Stats)
            {
                if (!defs.TryGet<StatDef>(change.Stat, out _))
                {
                    yield return $"uses[{i}]: unknown stat {change.Stat}";
                }
            }
        }
    }
}

/// <summary>Применение предмета действием: длительность, тратится ли предмет и как меняются статы.</summary>
public sealed record ItemUseDef
{
    /// <summary>Id действия.</summary>
    public required string Action { get; init; }

    /// <summary>Длительность в секундах симуляции.</summary>
    public required double Seconds { get; init; }

    /// <summary>Исчезает ли предмет после действия.</summary>
    public bool Consume { get; init; }

    /// <summary>Изменения статов по окончании действия.</summary>
    public IReadOnlyList<StatChangeDef> Stats { get; init; } = [];
}

/// <summary>Изменение стата: прибавка, отрицательная - убавка.</summary>
public sealed record StatChangeDef
{
    /// <summary>Id стата.</summary>
    public required string Stat { get; init; }

    /// <summary>Прибавка к значению.</summary>
    public required double Add { get; init; }
}
