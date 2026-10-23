namespace DevAInt.Sim.Stats;

/// <summary>Игровое время: номер тика и шаг тика с учётом ускорения всей симуляции.</summary>
public sealed class GameClock(double timeScale = 1)
{
    /// <summary>Тиков в реальную секунду.</summary>
    public const int TicksPerSecond = 10;

    /// <summary>Реальных секунд между тиками: по ним идут цикл тиков и интерполяция на клиенте.</summary>
    public const double RealSecondsPerTick = 1.0 / TicksPerSecond;

    /// <summary>Игровых минут за тик без ускорения: игровые сутки за реальный час, как в Zomboid.</summary>
    public const double DefaultMinutesPerTick = 24.0 * 60.0 / (60.0 * 60.0 * TicksPerSecond);

    /// <summary>Номер текущего тика.</summary>
    public long Tick { get; private set; }

    /// <summary>Ускорение всей симуляции: и статов, и движения; 1 - обычная скорость.</summary>
    public double TimeScale { get; } = timeScale;

    /// <summary>Игровых минут за тик, в них считаются статы.</summary>
    public double MinutesPerTick => DefaultMinutesPerTick * TimeScale;

    /// <summary>Секунд симуляции за тик, в них считается движение.</summary>
    public double SecondsPerTick => RealSecondsPerTick * TimeScale;

    /// <summary>Игровых минут с начала.</summary>
    public double Minutes => Tick * MinutesPerTick;

    /// <summary>Переход к следующему тику.</summary>
    public void Advance() => Tick++;
}
