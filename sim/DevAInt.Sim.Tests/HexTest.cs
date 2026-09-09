using System;
using System.Linq;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты <see cref="Hex"/>: шесть направлений, соседи, дистанция,
/// конверсия аксиал ↔ offset (odd-r, pointy-top).
/// </summary>
public class HexTest
{
    /// <summary>Шесть направлений: различны, единичной длины, в сумме дают ноль.</summary>
    [Fact]
    public void DirectionsAreSixUnitVectorsSummingToZero()
    {
        Hex[] dirs = Hex.Directions;

        Assert.Equal(6, dirs.Length);
        Assert.Equal(6, dirs.Distinct().Count());
        Assert.All(dirs, d => Assert.Equal(1, new Hex(0, 0).DistanceTo(d)));
        Assert.Equal(0, dirs.Sum(d => d.Q));
        Assert.Equal(0, dirs.Sum(d => d.R));
    }

    /// <summary>Порядок направлений зафиксирован по Red Blob: против часовой от (1, 0).</summary>
    [Fact]
    public void DirectionsOrderIsFixed()
    {
        Hex[] expected = [new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1)];

        Assert.Equal(expected, Hex.Directions);
    }

    /// <summary>Сосед по направлению = сдвиг на вектор направления.</summary>
    [Fact]
    public void NeighborIsShiftByDirection()
    {
        Hex origin = new(2, -3);

        for (int dir = 0; dir < 6; dir++)
        {
            Hex expected = new(origin.Q + Hex.Directions[dir].Q, origin.R + Hex.Directions[dir].R);
            Assert.Equal(expected, origin.Neighbor(dir));
        }
    }

    /// <summary>Дистанция через кубические координаты.</summary>
    /// <param name="q1">Q первого гекса.</param>
    /// <param name="r1">R первого гекса.</param>
    /// <param name="q2">Q второго гекса.</param>
    /// <param name="r2">R второго гекса.</param>
    /// <param name="expected">Ожидаемая дистанция.</param>
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(0, 0, 3, -1, 3)]
    [InlineData(0, 0, 2, 2, 4)]
    [InlineData(-2, 1, 1, -3, 4)]
    [InlineData(5, 5, 5, -5, 10)]
    public void DistanceMatchesCubeMetric(int q1, int r1, int q2, int r2, int expected)
    {
        Hex a = new(q1, r1);
        Hex b = new(q2, r2);

        Assert.Equal(expected, a.DistanceTo(b));
        Assert.Equal(expected, b.DistanceTo(a));
    }

    /// <summary>Неравенство треугольника на выборке.</summary>
    [Fact]
    public void DistanceSatisfiesTriangleInequality()
    {
        Hex a = new(0, 0);
        Hex b = new(4, -2);
        Hex c = new(-3, 5);

        Assert.True(a.DistanceTo(c) <= a.DistanceTo(b) + b.DistanceTo(c));
    }

    /// <summary>Offset odd-r → аксиал по формуле q = col − (row − (row &amp; 1)) / 2.</summary>
    /// <param name="col">Колонка.</param>
    /// <param name="row">Ряд.</param>
    /// <param name="q">Ожидаемый Q.</param>
    /// <param name="r">Ожидаемый R.</param>
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 1, 0, 1)]
    [InlineData(2, 3, 1, 3)]
    [InlineData(3, 2, 2, 2)]
    [InlineData(23, 17, 15, 17)]
    public void FromOffsetUsesOddR(int col, int row, int q, int r) => Assert.Equal(new Hex(q, r), Hex.FromOffset(col, row));

    /// <summary>Аксиал → offset — обратная формула.</summary>
    /// <param name="q">Q.</param>
    /// <param name="r">R.</param>
    /// <param name="col">Ожидаемая колонка.</param>
    /// <param name="row">Ожидаемый ряд.</param>
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 3, 2, 3)]
    [InlineData(2, 2, 3, 2)]
    [InlineData(15, 17, 23, 17)]
    public void ToOffsetIsInverseOfFromOffset(int q, int r, int col, int row) => Assert.Equal((col, row), new Hex(q, r).ToOffset());

    /// <summary>Round-trip на всей карте 24×18: FromOffset(ToOffset(h)) == h.</summary>
    [Fact]
    public void OffsetRoundTripOnWholeMap()
    {
        for (int row = 0; row < 18; row++)
        {
            for (int col = 0; col < 24; col++)
            {
                Hex hex = Hex.FromOffset(col, row);
                Assert.Equal((col, row), hex.ToOffset());
            }
        }
    }

    /// <summary>Соседи в offset-мире: у гекса чётного и нечётного ряда — разные колонки соседей.</summary>
    [Fact]
    public void NeighborsInOffsetSpaceFollowOddR()
    {
        (int, int)[] evenRow = Neighbors(Hex.FromOffset(4, 4));
        (int, int)[] oddRow = Neighbors(Hex.FromOffset(4, 5));

        Assert.Contains((3, 3), evenRow);
        Assert.Contains((4, 3), evenRow);
        Assert.DoesNotContain((5, 3), evenRow);
        Assert.Contains((4, 4), oddRow);
        Assert.Contains((5, 4), oddRow);
        Assert.DoesNotContain((3, 4), oddRow);
    }

    /// <summary>Линия между гексами: начинается в a, кончается в b, длина = дистанция + 1, шаги по соседям.</summary>
    /// <param name="q1">Q начала.</param>
    /// <param name="r1">R начала.</param>
    /// <param name="q2">Q конца.</param>
    /// <param name="r2">R конца.</param>
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 3, 0)]
    [InlineData(0, 0, 3, -1)]
    [InlineData(-2, 1, 4, 2)]
    [InlineData(5, 5, -3, 6)]
    public void LineToWalksNeighborByNeighbor(int q1, int r1, int q2, int r2)
    {
        Hex a = new(q1, r1);
        Hex b = new(q2, r2);

        Hex[] line = a.LineTo(b).ToArray();

        Assert.Equal(a.DistanceTo(b) + 1, line.Length);
        Assert.Equal(a, line[0]);
        Assert.Equal(b, line[^1]);
        for (int i = 1; i < line.Length; i++)
        {
            Assert.Equal(1, line[i - 1].DistanceTo(line[i]));
        }
    }

    /// <summary>Линия детерминирована и не зависит от направления, кроме порядка.</summary>
    [Fact]
    public void LineToIsSymmetric()
    {
        Hex a = new(1, -4);
        Hex b = new(6, 2);

        Hex[] forward = a.LineTo(b).ToArray();
        Hex[] backward = b.LineTo(a).Reverse().ToArray();

        Assert.Equal(forward, backward);
    }

    /// <summary>Соседи гекса в offset-координатах.</summary>
    /// <param name="hex">Гекс.</param>
    /// <returns>Шесть пар (col, row).</returns>
    private static (int, int)[] Neighbors(Hex hex) =>
        [.. Enumerable.Range(0, 6).Select(dir => hex.Neighbor(dir).ToOffset())];
}
