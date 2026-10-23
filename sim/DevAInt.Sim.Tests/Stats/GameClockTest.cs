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

    /// <summary>Скорость времени задаётся при создании.</summary>
    [Fact]
    public void MinutesFollowSpeed()
    {
        GameClock clock = new(minutesPerTick: 2);

        clock.Advance();
        clock.Advance();

        Assert.Equal(4, clock.Minutes);
    }
}
