using System;
using System.Linq;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты <see cref="Rng"/>: детерминизм от сида, диапазоны, равномерность на глаз,
/// выбор из списка.
/// </summary>
public class RngTest
{
    /// <summary>Одинаковый сид — одинаковая последовательность (основа реплеев и lockstep).</summary>
    [Fact]
    public void SameSeedSameSequence()
    {
        Rng a = new(42);
        Rng b = new(42);

        for (int idx = 0; idx < 1000; idx++)
        {
            Assert.Equal(a.NextU64(), b.NextU64());
        }
    }

    /// <summary>Разные сиды — разные последовательности.</summary>
    [Fact]
    public void DifferentSeedsDiffer()
    {
        Rng a = new(42);
        Rng b = new(43);

        ulong[] fromA = [.. Enumerable.Range(0, 16).Select(_ => a.NextU64())];
        ulong[] fromB = [.. Enumerable.Range(0, 16).Select(_ => b.NextU64())];

        Assert.NotEqual(fromA, fromB);
    }

    /// <summary>Сид 0 и отрицательный сид тоже дают живую последовательность, а не нули.</summary>
    /// <param name="seed">Сид.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void EdgeSeedsProduceVariedOutput(int seed)
    {
        Rng rng = new(seed);

        ulong[] values = [.. Enumerable.Range(0, 8).Select(_ => rng.NextU64())];

        Assert.True(values.Distinct().Count() >= 7, "edge seed must not collapse to a constant stream");
    }

    /// <summary>Next(max) — строго в [0, max) и покрывает все значения.</summary>
    [Fact]
    public void NextCoversRangeExclusive()
    {
        Rng rng = new(7);
        int[] hits = new int[6];

        for (int idx = 0; idx < 10_000; idx++)
        {
            int value = rng.Next(6);
            Assert.InRange(value, 0, 5);
            hits[value]++;
        }

        Assert.All(hits, h => Assert.InRange(h, 1_200, 2_200));
    }

    /// <summary>Next(1) — всегда 0; Next(0) и отрицательные — ошибка аргумента.</summary>
    [Fact]
    public void NextDegenerateBounds()
    {
        Rng rng = new(1);

        Assert.Equal(0, rng.Next(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(-5));
    }

    /// <summary>NextFloat — в [0, 1), не вырождается в одно значение.</summary>
    [Fact]
    public void NextFloatIsUnitInterval()
    {
        Rng rng = new(99);
        float[] values = [.. Enumerable.Range(0, 1000).Select(_ => rng.NextFloat())];

        Assert.All(values, v => Assert.InRange(v, 0f, 0.99999994f));
        Assert.True(values.Min() < 0.1f, "some values fall near 0");
        Assert.True(values.Max() > 0.9f, "some values rise near 1");
    }

    /// <summary>Chance(0) никогда, Chance(1) всегда, Chance(0.3) — примерно 30 %.</summary>
    [Fact]
    public void ChanceMatchesProbability()
    {
        Rng rng = new(5);
        int never = 0;
        int always = 0;
        int third = 0;

        for (int idx = 0; idx < 10_000; idx++)
        {
            never += rng.Chance(0f) ? 1 : 0;
            always += rng.Chance(1f) ? 1 : 0;
            third += rng.Chance(0.3f) ? 1 : 0;
        }

        Assert.Equal(0, never);
        Assert.Equal(10_000, always);
        Assert.InRange(third, 2_700, 3_300);
    }

    /// <summary>Pick — элемент списка; за много вызовов достаёт каждый; пустой список — ошибка.</summary>
    [Fact]
    public void PickReturnsListElements()
    {
        Rng rng = new(3);
        string[] items = ["a", "b", "c", "d"];

        string[] picked = [.. Enumerable.Range(0, 200).Select(_ => rng.Pick(items))];

        Assert.All(picked, p => Assert.Contains(p, items));
        Assert.Equal(4, picked.Distinct().Count());
        Assert.Throws<ArgumentException>(() => rng.Pick(Array.Empty<string>()));
    }

    /// <summary>Разные методы читают один поток: порядок вызовов влияет на результат.</summary>
    [Fact]
    public void MethodsShareOneStream()
    {
        Rng a = new(11);
        Rng b = new(11);

        _ = a.Next(100);
        int afterSkip = a.Next(100);
        int noSkip = b.Next(100);

        Assert.NotEqual(afterSkip, noSkip);
    }
}
