using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Створка в стене: дверь в дверном проёме или окно в оконной раме; открывается и закрывается на E.</summary>
public abstract record FillDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Цвет в виде #rrggbb, пока моделей и текстур нет.</summary>
    public string Color { get; init; } = "#808080";

    /// <summary>Стена с проёмом или рамой, которую ставит операция карты вместе со створкой.</summary>
    public required string Frame { get; init; }

    /// <summary>Как створка ставится в проём или раму игроком, null - не ставится и не снимается.</summary>
    public BuildDef? Build { get; init; }

    /// <summary>Вид стены, в который створка встаёт.</summary>
    public abstract WallKind FrameKind { get; }

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        foreach (string problem in DefColor.Validate(Color))
        {
            yield return problem;
        }

        if (!defs.TryGet<WallDef>(Frame, out WallDef? frame) || frame.Kind != FrameKind)
        {
            yield return $"frame {Frame} must be a wall of kind {FrameKind}";
        }

        foreach (string problem in Build?.Validate(defs) ?? [])
        {
            yield return problem;
        }
    }
}

/// <summary>Дверь: закрытая держит проход и взгляд, открытая пропускает.</summary>
public sealed record DoorDef : FillDef
{
    /// <inheritdoc/>
    public override WallKind FrameKind => WallKind.DoorFrame;
}

/// <summary>Окно: стекло прозрачное, через окно видно и закрытое, но пройти нельзя.</summary>
public sealed record WindowDef : FillDef
{
    /// <inheritdoc/>
    public override WallKind FrameKind => WallKind.WindowFrame;
}
