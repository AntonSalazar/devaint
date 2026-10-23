using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Вид стены на ребре клетки.</summary>
public enum WallKind
{
    /// <summary>Сплошная стена.</summary>
    Wall,

    /// <summary>Дверь, открыта или закрыта.</summary>
    Door,
}

/// <summary>Определение стены или двери на ребре клетки.</summary>
public sealed record WallDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Вид: стена или дверь.</summary>
    public required WallKind Kind { get; init; }

    /// <summary>Цвет в виде #rrggbb, пока моделей и текстур нет.</summary>
    public string Color { get; init; } = "#a0a0a0";

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs) => DefColor.Validate(Color);
}
