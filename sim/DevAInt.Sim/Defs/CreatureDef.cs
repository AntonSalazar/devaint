using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Motion;

namespace DevAInt.Sim.Defs;

/// <summary>Вид существа: тело, свои статы, атака и поведение; игрок тоже существо.</summary>
public sealed record CreatureDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Цвет в виде #rrggbb, пока моделей нет.</summary>
    public string Color { get; init; } = "#808080";

    /// <summary>Радиус тела в метрах.</summary>
    public double Radius { get; init; } = MoveParams.Default.Radius;

    /// <summary>Скорость ходьбы в метрах в секунду симуляции.</summary>
    public double Speed { get; init; } = MoveParams.Default.Speed;

    /// <summary>Статы, которые есть у существа; остальные у него не меняются и не дают мудлов.</summary>
    public IReadOnlyList<string> Stats { get; init; } = [];

    /// <summary>Атака или null, если существо не атакует.</summary>
    public AttackDef? Attack { get; init; }

    /// <summary>Поведение или null, если существом управляет игрок.</summary>
    public BrainDef? Brain { get; init; }

    /// <summary>Тело для движения.</summary>
    public MoveParams Body => new(Radius, Speed);

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        foreach (string problem in DefColor.Validate(Color))
        {
            yield return problem;
        }

        // Тело должно пройти в дверной проём в одну клетку
        if (Radius is <= 0 or >= 0.5)
        {
            yield return $"radius {Radius} must be within 0..0.5";
        }

        if (Speed <= 0)
        {
            yield return $"speed {Speed} must be positive";
        }

        foreach (string stat in Stats.Where(stat => !defs.TryGet<StatDef>(stat, out _)))
        {
            yield return $"stats: unknown stat {stat}";
        }

        foreach (string problem in Attack?.Validate(defs) ?? [])
        {
            yield return $"attack: {problem}";
        }

        foreach (string problem in Brain?.Validate(defs) ?? [])
        {
            yield return $"brain: {problem}";
        }
    }
}

/// <summary>Атака: дальность от центра до центра, перезарядка и изменение стата цели.</summary>
public sealed record AttackDef
{
    /// <summary>Дальность в метрах.</summary>
    public required double Range { get; init; }

    /// <summary>Перезарядка в секундах симуляции.</summary>
    public required double Seconds { get; init; }

    /// <summary>Стат цели.</summary>
    public required string Stat { get; init; }

    /// <summary>Прибавка к стату цели, урон - отрицательная.</summary>
    public required double Add { get; init; }

    /// <summary>Проблемы атаки.</summary>
    internal IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Range <= 0)
        {
            yield return $"range {Range} must be positive";
        }

        if (Seconds <= 0)
        {
            yield return $"seconds {Seconds} must be positive";
        }

        if (!defs.TryGet<StatDef>(Stat, out _))
        {
            yield return $"unknown stat {Stat}";
        }
    }
}

/// <summary>Поведение "заметить, догнать, атаковать": радиус зрения и виды существ-целей.</summary>
public sealed record BrainDef
{
    /// <summary>Радиус зрения в метрах, стены и закрытые двери загораживают.</summary>
    public required double Sight { get; init; }

    /// <summary>Виды существ, за которыми гнаться.</summary>
    public required IReadOnlyList<string> Targets { get; init; }

    /// <summary>Проблемы поведения.</summary>
    internal IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Sight <= 0)
        {
            yield return $"sight {Sight} must be positive";
        }

        foreach (string target in Targets.Where(target => !defs.TryGet<CreatureDef>(target, out _)))
        {
            yield return $"unknown target creature {target}";
        }
    }
}
