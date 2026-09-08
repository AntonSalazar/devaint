using System;

using DevAInt.Sim;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты <see cref="Layer{T}"/>: размеры, заполнение, индексация по <see cref="Hex"/>,
/// границы, сырой буфер в порядке row-major.
/// </summary>
public class LayerTest
{
    /// <summary>Размеры сохраняются, буфер — width × height.</summary>
    [Fact]
    public void SizeAndRawLength()
    {
        Layer<int> layer = new(24, 18);

        Assert.Equal(24, layer.Width);
        Assert.Equal(18, layer.Height);
        Assert.Equal(24 * 18, layer.Raw.Length);
    }

    /// <summary>Значение по умолчанию — default(T); заполнение через конструктор.</summary>
    [Fact]
    public void DefaultAndFillValues()
    {
        Layer<int> empty = new(3, 2);
        Layer<int> filled = new(3, 2, fill: -1);

        Assert.Equal(0, empty[Hex.FromOffset(2, 1)]);
        Assert.Equal(-1, filled[Hex.FromOffset(0, 0)]);
        Assert.Equal(-1, filled[Hex.FromOffset(2, 1)]);
    }

    /// <summary>Запись и чтение по гексу; соседняя ячейка не затронута.</summary>
    [Fact]
    public void IndexerReadsWhatWasWritten()
    {
        Layer<float> layer = new(5, 5);
        Hex target = Hex.FromOffset(2, 3);
        Hex other = Hex.FromOffset(3, 3);

        layer[target] = 0.75f;

        Assert.Equal(0.75f, layer[target]);
        Assert.Equal(0f, layer[other]);
    }

    /// <summary>Contains — по offset-границам, включая гексы с отрицательным Q в нечётных рядах.</summary>
    [Fact]
    public void ContainsFollowsOffsetBounds()
    {
        Layer<byte> layer = new(4, 3);

        Assert.True(layer.Contains(Hex.FromOffset(0, 0)));
        Assert.True(layer.Contains(Hex.FromOffset(3, 2)));
        Assert.True(layer.Contains(Hex.FromOffset(0, 1)));
        Assert.False(layer.Contains(Hex.FromOffset(4, 0)));
        Assert.False(layer.Contains(Hex.FromOffset(0, 3)));
        Assert.False(layer.Contains(Hex.FromOffset(-1, 0)));
        Assert.False(layer.Contains(new Hex(0, -1)));
    }

    /// <summary>Индексатор за границей бросает ArgumentOutOfRangeException, а не молча читает соседа.</summary>
    [Fact]
    public void IndexerOutOfBoundsThrows()
    {
        Layer<int> layer = new(4, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => layer[Hex.FromOffset(4, 0)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => layer[Hex.FromOffset(-1, 2)] = 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => layer[new Hex(0, 3)]);
    }

    /// <summary>Raw — row-major: индекс = row × Width + col (нужно для упаковки в текстуру).</summary>
    [Fact]
    public void RawIsRowMajor()
    {
        Layer<int> layer = new(4, 3);
        layer[Hex.FromOffset(1, 2)] = 7;
        layer[Hex.FromOffset(3, 0)] = 9;

        Assert.Equal(7, layer.Raw[(2 * 4) + 1]);
        Assert.Equal(9, layer.Raw[3]);
    }

    /// <summary>Нулевые размеры запрещены.</summary>
    [Fact]
    public void ZeroSizeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Layer<int>(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Layer<int>(3, 0));
    }
}
