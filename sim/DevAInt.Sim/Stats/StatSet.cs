using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Stats;

/// <summary>Модификатор скорости стата с владельцем, по которому его снимают.</summary>
public readonly record struct Modifier(string Owner, ModifierKind Kind, double Value);

/// <summary>Смена уровня мудла за тик, уровень 0 - мудла нет.</summary>
public readonly record struct MoodleChange(int Moodle, int OldLevel, int NewLevel);

/// <summary>Статы одной сущности: значения, модификаторы скорости и уровни мудлов.</summary>
public sealed class StatSet
{
    /// <summary>Допуск порога: набегающее сложение может дать 39.9999999 вместо 40.</summary>
    private const double ThresholdEpsilon = 1e-6;

    /// <summary>Общая таблица статов и мудлов.</summary>
    private readonly StatTable _table;

    /// <summary>Значения статов по номерам.</summary>
    private readonly double[] _values;

    /// <summary>Итоговые скорости с модификаторами, пересчитываются при изменении модификаторов.</summary>
    private readonly double[] _rates;

    /// <summary>Модификаторы скорости по номерам статов.</summary>
    private readonly List<Modifier>[] _modifiers;

    /// <summary>Текущие уровни мудлов по номерам.</summary>
    private readonly int[] _moodleLevels;

    /// <summary>Смены уровней мудлов за последний тик.</summary>
    private readonly List<MoodleChange> _changes = [];

    /// <summary>Статы на минимуме, мудлы по начальным значениям.</summary>
    public StatSet(StatTable table)
    {
        _table = table;
        _values = new double[table.StatCount];
        _rates = new double[table.StatCount];
        _modifiers = new List<Modifier>[table.StatCount];
        _moodleLevels = new int[table.MoodleCount];
        for (int i = 0; i < table.StatCount; i++)
        {
            _values[i] = table.Stat(i).Min;
            _modifiers[i] = [];
            RecalculateRate(i);
        }

        // Мудлы начального состояния не считаются сменой
        UpdateMoodles();
        _changes.Clear();
    }

    /// <summary>Смены уровней мудлов за последний тик.</summary>
    public IReadOnlyList<MoodleChange> Changes => _changes;

    /// <summary>Значение стата.</summary>
    public double Value(int stat) => _values[stat];

    /// <summary>Итоговая скорость стата за игровую минуту.</summary>
    public double Rate(int stat) => _rates[stat];

    /// <summary>Уровень мудла, 0 - мудла нет.</summary>
    public int MoodleLevel(int moodle) => _moodleLevels[moodle];

    /// <summary>Установка значения в пределах диапазона; мудлы обновятся в следующем тике.</summary>
    public void SetValue(int stat, double value)
    {
        StatDef def = _table.Stat(stat);
        _values[stat] = Math.Clamp(value, def.Min, def.Max);
    }

    /// <summary>Добавление модификатора скорости стата.</summary>
    public void AddModifier(int stat, Modifier modifier)
    {
        _modifiers[stat].Add(modifier);
        RecalculateRate(stat);
    }

    /// <summary>Снятие всех модификаторов владельца со всех статов.</summary>
    public void RemoveModifiers(string owner)
    {
        for (int i = 0; i < _modifiers.Length; i++)
        {
            if (_modifiers[i].RemoveAll(modifier => modifier.Owner == owner) > 0)
            {
                RecalculateRate(i);
            }
        }
    }

    /// <summary>Тик: сначала все значения по скоростям, затем уровни мудлов.</summary>
    public void Tick(double minutes)
    {
        _changes.Clear();
        for (int i = 0; i < _values.Length; i++)
        {
            StatDef def = _table.Stat(i);
            _values[i] = Math.Clamp(_values[i] + (_rates[i] * minutes), def.Min, def.Max);
        }

        // Эффекты новых уровней меняют скорости, поэтому действуют со следующего тика
        UpdateMoodles();
    }

    /// <summary>Наибольший уровень, порог которого достигнут, 0 если ни одного.</summary>
    private static int LevelFor(MoodleDef moodle, double value)
    {
        int level = 0;
        while (level < moodle.Levels.Count && value >= moodle.Levels[level].At - ThresholdEpsilon)
        {
            level++;
        }

        return level;
    }

    /// <summary>Пересчёт уровней мудлов и смена их модификаторов.</summary>
    private void UpdateMoodles()
    {
        for (int i = 0; i < _moodleLevels.Length; i++)
        {
            MoodleInfo moodle = _table.Moodle(i);
            int level = LevelFor(moodle.Def, _values[moodle.Stat]);
            int old = _moodleLevels[i];
            if (level == old)
            {
                continue;
            }

            RemoveModifiers(moodle.Owner);
            if (level > 0)
            {
                foreach (ResolvedEffect effect in moodle.EffectsByLevel[level - 1])
                {
                    AddModifier(effect.Stat, new Modifier(moodle.Owner, effect.Kind, effect.Value));
                }
            }

            _moodleLevels[i] = level;
            _changes.Add(new MoodleChange(i, old, level));
        }
    }

    /// <summary>Итоговая скорость: базовая плюс прибавки, умноженные на все множители.</summary>
    private void RecalculateRate(int stat)
    {
        double add = 0;
        double multiply = 1;
        foreach (Modifier modifier in _modifiers[stat])
        {
            if (modifier.Kind == ModifierKind.Add)
            {
                add += modifier.Value;
            }
            else
            {
                multiply *= modifier.Value;
            }
        }

        _rates[stat] = (_table.Stat(stat).Rate + add) * multiply;
    }
}
