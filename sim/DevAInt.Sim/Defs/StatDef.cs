using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Mods;

namespace DevAInt.Sim.Defs;

/// <summary>Определение стата: диапазон, скорость изменения и мудлы на порогах.</summary>
public sealed record StatDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Нижняя граница значения, с неё стат начинается.</summary>
    public required double Min { get; init; }

    /// <summary>Верхняя граница значения.</summary>
    public required double Max { get; init; }

    /// <summary>Изменение значения за игровую минуту.</summary>
    public double Rate { get; init; }

    /// <summary>Мудлы, которые появляются на порогах стата.</summary>
    public IReadOnlyList<MoodleDef> Moodles { get; init; } = [];

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Min >= Max)
        {
            yield return $"min {Min} must be less than max {Max}";
        }

        foreach (MoodleDef moodle in Moodles)
        {
            foreach (string problem in ValidateMoodle(moodle, defs))
            {
                yield return $"moodle {moodle.Id}: {problem}";
            }
        }
    }

    /// <summary>Проверка одного мудла: id, пороги и эффекты.</summary>
    private IEnumerable<string> ValidateMoodle(MoodleDef moodle, DefDatabase defs)
    {
        if (!ModIds.IsDefId(moodle.Id))
        {
            yield return "moodle id must look like mod:name using a-z, 0-9 and _";
        }

        if (Moodles.Count(other => other.Id == moodle.Id) > 1)
        {
            yield return "duplicate moodle id";
        }

        // Id мудла уникален во всех статах: по нему мудл ищут Lua-хуки
        foreach (StatDef other in defs.All<StatDef>())
        {
            if (other.Id != Id && other.Moodles.Any(item => item.Id == moodle.Id))
            {
                yield return $"moodle id is already used by stat {other.Id}";
            }
        }

        if (moodle.Levels.Count == 0)
        {
            yield return "moodle needs at least one level";
        }

        for (int i = 0; i < moodle.Levels.Count; i++)
        {
            MoodleLevelDef level = moodle.Levels[i];
            if (level.At < Min || level.At > Max)
            {
                yield return $"level {i + 1} threshold {level.At} is outside {Min}..{Max}";
            }

            if (i > 0 && level.At <= moodle.Levels[i - 1].At)
            {
                yield return $"level {i + 1} threshold {level.At} must be greater than level {i} threshold";
            }

            foreach (EffectDef effect in level.Effects)
            {
                if (!defs.TryGet<StatDef>(effect.Stat, out _))
                {
                    yield return $"level {i + 1} effect targets unknown stat {effect.Stat}";
                }

                if (effect.Kind == ModifierKind.Multiply && effect.Value < 0)
                {
                    yield return $"level {i + 1} effect multiplier {effect.Value} must not be negative";
                }
            }
        }
    }
}
