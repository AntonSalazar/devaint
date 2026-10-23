namespace DevAInt.Sim.Defs;

/// <summary>Вид модификатора скорости стата.</summary>
public enum ModifierKind
{
    /// <summary>Прибавка к базовой скорости.</summary>
    Add,

    /// <summary>Множитель скорости.</summary>
    Multiply,
}

/// <summary>Эффект: модификатор скорости стата.</summary>
public sealed record EffectDef
{
    /// <summary>Id стата, скорость которого меняется.</summary>
    public required string Stat { get; init; }

    /// <summary>Вид модификатора.</summary>
    public required ModifierKind Kind { get; init; }

    /// <summary>Прибавка или множитель.</summary>
    public required double Value { get; init; }
}
