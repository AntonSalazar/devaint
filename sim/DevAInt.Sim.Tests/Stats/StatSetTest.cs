using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Stats;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Stats;

/// <summary>Тесты <see cref="StatSet"/>: значения, модификаторы и мудлы.</summary>
public class StatSetTest
{
    /// <summary>Таблица статов настоящего мода core.</summary>
    private static readonly StatTable _core = LoadCore();

    /// <summary>Итог этапа 2: через 800 игровых минут голод даёт мудл Голоден I.</summary>
    [Fact]
    public void Hunger800MinutesGivesHungryOne()
    {
        StatSet stats = new(_core);
        GameClock clock = new();
        int hungry = _core.MoodleIndex("core:hungry");

        // Тикаем с обычной скоростью времени, как сервер
        while (clock.Minutes < 799)
        {
            clock.Advance();
            stats.Tick(clock.MinutesPerTick);
        }

        Assert.Equal(0, stats.MoodleLevel(hungry));

        while (clock.Minutes < 800 - 1e-9)
        {
            clock.Advance();
            stats.Tick(clock.MinutesPerTick);
        }

        Assert.Equal(40, stats.Value(_core.StatIndex("core:hunger")), precision: 6);
        Assert.Equal(1, stats.MoodleLevel(hungry));
    }

    /// <summary>Итог этапа 2: эффект мудла меняет скорость другого стата и снимается вместе с мудлом.</summary>
    [Fact]
    public void MoodleEffectChangesOtherStatAndIsRemoved()
    {
        StatSet stats = new(_core);
        int hunger = _core.StatIndex("core:hunger");
        int fatigue = _core.StatIndex("core:fatigue");

        stats.SetValue(hunger, 75);
        stats.Tick(0);
        Assert.Equal(0.04 * 1.5, stats.Rate(fatigue), precision: 9);

        stats.SetValue(hunger, 95);
        stats.Tick(0);
        Assert.Equal(0.04 * 2, stats.Rate(fatigue), precision: 9);

        stats.SetValue(hunger, 10);
        stats.Tick(0);
        Assert.Equal(0.04, stats.Rate(fatigue), precision: 9);
    }

    /// <summary>Значение не выходит за диапазон стата.</summary>
    [Fact]
    public void ValueStaysInRange()
    {
        StatSet stats = new(_core);
        int hunger = _core.StatIndex("core:hunger");

        stats.Tick(100_000);
        Assert.Equal(100, stats.Value(hunger));

        stats.SetValue(hunger, -5);
        Assert.Equal(0, stats.Value(hunger));
    }

    /// <summary>Смены уровней записываются в обе стороны и очищаются следующим тиком.</summary>
    [Fact]
    public void ChangesAreRecordedBothWays()
    {
        StatSet stats = new(_core);
        int hunger = _core.StatIndex("core:hunger");
        int hungry = _core.MoodleIndex("core:hungry");

        stats.SetValue(hunger, 95);
        stats.Tick(0);
        Assert.Equal([new MoodleChange(hungry, 0, 3)], stats.Changes);

        stats.SetValue(hunger, 45);
        stats.Tick(0);
        Assert.Equal([new MoodleChange(hungry, 3, 1)], stats.Changes);

        stats.Tick(0);
        Assert.Empty(stats.Changes);
    }

    /// <summary>Итоговая скорость: базовая плюс прибавки, умноженные на множители; снятие по владельцу.</summary>
    [Fact]
    public void ModifiersCombineAndAreRemovedByOwner()
    {
        StatSet stats = new(_core);
        int thirst = _core.StatIndex("core:thirst");

        stats.AddModifier(thirst, new Modifier("item:a", ModifierKind.Add, 0.02));
        stats.AddModifier(thirst, new Modifier("item:b", ModifierKind.Multiply, 2));
        stats.AddModifier(thirst, new Modifier("item:b", ModifierKind.Multiply, 1.5));
        Assert.Equal((0.08 + 0.02) * 2 * 1.5, stats.Rate(thirst), precision: 9);

        stats.RemoveModifiers("item:b");
        Assert.Equal(0.08 + 0.02, stats.Rate(thirst), precision: 9);

        stats.RemoveModifiers("item:a");
        Assert.Equal(0.08, stats.Rate(thirst), precision: 9);
    }

    /// <summary>Модификатор скорости влияет на накопление значения.</summary>
    [Fact]
    public void RateModifierAffectsTick()
    {
        StatSet stats = new(_core);
        int thirst = _core.StatIndex("core:thirst");

        stats.AddModifier(thirst, new Modifier("test", ModifierKind.Multiply, 0));
        stats.Tick(100);

        Assert.Equal(0, stats.Value(thirst));
    }

    /// <summary>Начальные мудлы не попадают в смены, статы начинаются с минимума.</summary>
    [Fact]
    public void StartsAtMinWithoutChanges()
    {
        StatSet stats = new(_core);

        Assert.Empty(stats.Changes);
        Assert.All(Enumerable.Range(0, _core.StatCount), i => Assert.Equal(_core.Stat(i).Min, stats.Value(i)));
        Assert.All(Enumerable.Range(0, _core.MoodleCount), i => Assert.Equal(0, stats.MoodleLevel(i)));
    }

    /// <summary>Таблица нумерует статы и мудлы по id и переводит эффекты в номера.</summary>
    [Fact]
    public void TableResolvesEffects()
    {
        MoodleInfo hungry = _core.Moodle(_core.MoodleIndex("core:hungry"));

        Assert.Equal(_core.StatIndex("core:hunger"), hungry.Stat);
        Assert.Equal("moodle:core:hungry", hungry.Owner);
        Assert.Empty(hungry.EffectsByLevel[0]);
        Assert.Equal(new ResolvedEffect(_core.StatIndex("core:fatigue"), ModifierKind.Multiply, 1.5), Assert.Single(hungry.EffectsByLevel[1]));
    }

    /// <summary>Загрузка core из папки mods репозитория.</summary>
    private static StatTable LoadCore()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return new StatTable(result.Defs);
    }
}
