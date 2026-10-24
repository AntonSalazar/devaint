using DevAInt.Sim.Stats;
using Xunit;

namespace DevAInt.Sim.Tests.Stats;

/// <summary>Тесты <see cref="TickTimer"/>.</summary>
public class TickTimerTest
{
    /// <summary>Кадры по 1/60 секунды дают тик каждые шесть кадров, остаток виден в доле.</summary>
    [Fact]
    public void FramesAccumulateIntoTicks()
    {
        TickTimer timer = new();
        int ticks = 0;

        for (int i = 0; i < 60; i++)
        {
            ticks += timer.Advance(1.0 / 60);
        }

        ticks += timer.Advance(0.05);

        Assert.Equal(10, ticks);
        Assert.Equal(0.5, timer.Fraction, 6);
    }

    /// <summary>После долгого зависания тиков не больше предела догона.</summary>
    [Fact]
    public void LongFrameIsCapped()
    {
        TickTimer timer = new(maxCatchUpTicks: 5);

        Assert.Equal(5, timer.Advance(10));
        Assert.Equal(0, timer.Fraction, 6);
    }
}
