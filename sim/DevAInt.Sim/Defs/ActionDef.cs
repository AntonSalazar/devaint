using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Определение действия: общие правила; что именно оно делает, задаёт применение предмета.</summary>
public sealed record ActionDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Прерывается ли действие, если сущность двинулась.</summary>
    public bool InterruptOnMove { get; init; } = true;

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs) => [];
}
