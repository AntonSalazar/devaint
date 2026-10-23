using System;
using System.Collections.Generic;
using System.Globalization;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;

/// <summary>Полоса стата в HUD: имя, значение и доля от min до max.</summary>
public readonly record struct StatBar(string Name, float Value, float Fraction);

/// <summary>Что показывает HUD своего персонажа: полосы статов и мудлы; данные только из реплики.</summary>
public sealed class HudModel(StatTable table, Replica replica)
{
    /// <summary>Римские цифры уровней мудлов.</summary>
    private static readonly string[] _roman = ["I", "II", "III", "IV", "V"];

    /// <summary>Имя из id до появления локализации: core:low_power - low power.</summary>
    public static string ShortName(string id) => id[(id.IndexOf(':') + 1)..].Replace('_', ' ');

    /// <summary>Полосы всех статов по номерам.</summary>
    public IReadOnlyList<StatBar> Bars()
    {
        List<StatBar> bars = new(table.StatCount);
        for (int i = 0; i < table.StatCount; i++)
        {
            StatDef stat = table.Stat(i);
            float value = replica.OwnStats[i];
            float fraction = (float)Math.Clamp((value - stat.Min) / (stat.Max - stat.Min), 0, 1);
            bars.Add(new StatBar(ShortName(stat.Id), value, fraction));
        }

        return bars;
    }

    /// <summary>Действующие мудлы своей сущности вида hungry II; пусто, пока сущности нет.</summary>
    public IReadOnlyList<string> Moodles()
    {
        List<string> moodles = [];
        if (replica.Entity(replica.OwnEntityId) is not ReplicaEntity own)
        {
            return moodles;
        }

        for (int i = 0; i < own.MoodleLevels.Length; i++)
        {
            int level = own.MoodleLevels[i];
            if (level > 0)
            {
                moodles.Add($"{ShortName(table.Moodle(i).Def.Id)} {Roman(level)}");
            }
        }

        return moodles;
    }

    /// <summary>Уровень римской цифрой, после V - обычным числом.</summary>
    private static string Roman(int level) => level <= _roman.Length ? _roman[level - 1] : level.ToString(CultureInfo.InvariantCulture);
}
