using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Определение объекта мира: мебель и всё, что стоит на клетках, занимает их и может мешать ходить и смотреть.</summary>
public sealed record ObjectDef : IDef
{
    /// <summary>Наибольшая сторона объекта в клетках: объект задевает не дальше соседнего чанка.</summary>
    public const int MaxSide = 8;

    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Ширина в клетках по x без поворота.</summary>
    public int W { get; init; } = 1;

    /// <summary>Глубина в клетках по y без поворота.</summary>
    public int H { get; init; } = 1;

    /// <summary>Высота в метрах.</summary>
    public double Height { get; init; } = 1;

    /// <summary>Цвет в виде #rrggbb, пока моделей нет.</summary>
    public string Color { get; init; } = "#808080";

    /// <summary>Мешает ли ходить через свои клетки.</summary>
    public bool BlocksMove { get; init; } = true;

    /// <summary>Закрывает ли обзор, как шкаф; стол не закрывает.</summary>
    public bool BlocksSight { get; init; }

    /// <summary>Контейнер с вместимостью, null - в объект ничего не положить.</summary>
    public ContainerDef? Container { get; init; }

    /// <summary>Как объект строится игроком, null - не строится и не разбирается.</summary>
    public BuildDef? Build { get; init; }

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        foreach (string problem in DefColor.Validate(Color))
        {
            yield return problem;
        }

        if (W < 1 || H < 1 || W > MaxSide || H > MaxSide)
        {
            yield return $"size {W}x{H} must be within 1..{MaxSide}";
        }

        if (Height <= 0)
        {
            yield return $"height {Height} must be positive";
        }

        foreach (string problem in Container?.Validate("container") ?? [])
        {
            yield return problem;
        }

        foreach (string problem in Build?.Validate(defs) ?? [])
        {
            yield return problem;
        }
    }
}
