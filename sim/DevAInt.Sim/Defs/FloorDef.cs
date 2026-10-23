using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Определение пола клетки.</summary>
public sealed record FloorDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs) => [];
}
