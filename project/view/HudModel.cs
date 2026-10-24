using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Items;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;

/// <summary>Полоса стата в HUD: имя, значение и доля от min до max.</summary>
public readonly record struct StatBar(string Name, float Value, float Fraction);

/// <summary>Что показывает HUD своего персонажа: полосы статов, мудлы, предметы и надпись о смерти; данные только из реплики.</summary>
public sealed class HudModel
{
    /// <summary>Сколько секунд видна надпись о смерти.</summary>
    public const double DeathMessageSeconds = 3;

    /// <summary>Римские цифры уровней мудлов.</summary>
    private static readonly string[] _roman = ["I", "II", "III", "IV", "V"];

    /// <summary>Статы и мудлы по номерам.</summary>
    private readonly StatTable _table;

    /// <summary>Предметы по номерам.</summary>
    private readonly ItemTable _items;

    /// <summary>Реплика своего клиента.</summary>
    private readonly Replica _replica;

    /// <summary>Сколько секунд ещё видна надпись о смерти.</summary>
    private double _deathLeft;

    /// <summary>Модель по таблицам и реплике; смена своей сущности значит, что прежняя умерла.</summary>
    public HudModel(StatTable table, ItemTable items, Replica replica)
    {
        _table = table;
        _items = items;
        _replica = replica;
        _replica.OwnEntityChanged += () => _deathLeft = DeathMessageSeconds;
    }

    /// <summary>Видна ли надпись о смерти.</summary>
    public bool DeathVisible => _deathLeft > 0;

    /// <summary>Имя из id до появления локализации: core:low_power - low power.</summary>
    public static string ShortName(string id) => id[(id.IndexOf(':') + 1)..].Replace('_', ' ');

    /// <summary>Ход времени надписи о смерти на delta секунд.</summary>
    public void Advance(double delta) => _deathLeft = Math.Max(0, _deathLeft - delta);

    /// <summary>Полосы всех статов по номерам.</summary>
    public IReadOnlyList<StatBar> Bars()
    {
        List<StatBar> bars = new(_table.StatCount);
        for (int i = 0; i < _table.StatCount; i++)
        {
            StatDef stat = _table.Stat(i);
            float value = _replica.OwnStats[i];
            float fraction = (float)Math.Clamp((value - stat.Min) / (stat.Max - stat.Min), 0, 1);
            bars.Add(new StatBar(ShortName(stat.Id), value, fraction));
        }

        return bars;
    }

    /// <summary>Действующие мудлы своей сущности вида hungry II; пусто, пока сущности нет.</summary>
    public IReadOnlyList<string> Moodles()
    {
        List<string> moodles = [];
        if (_replica.Entity(_replica.OwnEntityId) is not ReplicaEntity own)
        {
            return moodles;
        }

        for (int i = 0; i < own.MoodleLevels.Length; i++)
        {
            int level = own.MoodleLevels[i];
            if (level > 0)
            {
                moodles.Add($"{ShortName(_table.Moodle(i).Def.Id)} {Roman(level)}");
            }
        }

        return moodles;
    }

    /// <summary>Свои предметы по видам в порядке первого появления: apple x3.</summary>
    public IReadOnlyList<string> Items()
    {
        List<string> lines = [];
        foreach (IGrouping<int, ItemData> group in _replica.Inventory.GroupBy(item => item.Item))
        {
            lines.Add($"{ShortName(_items.Item(group.Key).Id)} x{group.Count()}");
        }

        return lines;
    }

    /// <summary>Уровень римской цифрой, после V - обычным числом.</summary>
    private static string Roman(int level) => level <= _roman.Length ? _roman[level - 1] : level.ToString(CultureInfo.InvariantCulture);
}
