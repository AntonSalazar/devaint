using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Определение пола клетки.</summary>
public sealed record FloorDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Цвет в виде #rrggbb, пока моделей и текстур нет.</summary>
    public string Color { get; init; } = "#808080";

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs) => DefColor.Validate(Color);
}
