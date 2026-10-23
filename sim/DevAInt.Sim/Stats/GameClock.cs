namespace DevAInt.Sim.Stats;

/// <summary>Игровое время: номер тика и игровые минуты за тик.</summary>
public sealed class GameClock(double minutesPerTick = GameClock.DefaultMinutesPerTick)
{
    /// <summary>Тиков в реальную секунду.</summary>
    public const int TicksPerSecond = 10;

    /// <summary>Игровых минут за тик по умолчанию: игровые сутки за реальный час, как в Zomboid.</summary>
    public const double DefaultMinutesPerTick = 24.0 * 60.0 / (60.0 * 60.0 * TicksPerSecond);

    /// <summary>Номер текущего тика.</summary>
    public long Tick { get; private set; }

    /// <summary>Игровых минут за тик.</summary>
    public double MinutesPerTick { get; } = minutesPerTick;

    /// <summary>Игровых минут с начала.</summary>
    public double Minutes => Tick * MinutesPerTick;

    /// <summary>Переход к следующему тику.</summary>
    public void Advance() => Tick++;
}
