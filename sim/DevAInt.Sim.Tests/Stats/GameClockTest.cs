using DevAInt.Sim.Stats;
using Xunit;

namespace DevAInt.Sim.Tests.Stats;

/// <summary>Тесты <see cref="GameClock"/>.</summary>
public class GameClockTest
{
    /// <summary>По умолчанию игровые сутки проходят за реальный час.</summary>
    [Fact]
    public void DefaultDayLastsOneRealHour()
    {
        GameClock clock = new();
        int ticksPerHour = 60 * 60 * GameClock.TicksPerSecond;

        for (int i = 0; i < ticksPerHour; i++)
        {
            clock.Advance();
        }

        Assert.Equal(ticksPerHour, clock.Tick);
        Assert.Equal(24 * 60, clock.Minutes, precision: 6);
    }

    /// <summary>Ускорение умножает и игровые минуты, и секунды симуляции за тик; реальный тик не меняется.</summary>
    [Fact]
    public void TimeScaleSpeedsUpEverything()
    {
        GameClock clock = new(timeScale: 50);

        clock.Advance();
        clock.Advance();

        Assert.Equal(2, clock.MinutesPerTick, precision: 9);
        Assert.Equal(5, clock.SecondsPerTick, precision: 9);
        Assert.Equal(4, clock.Minutes, precision: 9);
        Assert.Equal(0.1, GameClock.RealSecondsPerTick);
    }
}
