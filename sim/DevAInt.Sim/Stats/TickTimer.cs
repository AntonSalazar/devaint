using System;

namespace DevAInt.Sim.Stats;

/// <summary>Счётчик реального времени до тиков: сколько тиков пора сделать за прошедший кадр.</summary>
public sealed class TickTimer(int maxCatchUpTicks = TickTimer.DefaultMaxCatchUpTicks)
{
    /// <summary>Наибольшее число тиков догона за кадр по умолчанию, чтобы после зависания не тикать без конца.</summary>
    public const int DefaultMaxCatchUpTicks = 5;

    /// <summary>Накопленное время до следующего тика.</summary>
    private double _accumulator;

    /// <summary>Доля пути от прошлого тика до следующего, от 0 до 1, для интерполяции.</summary>
    public double Fraction => _accumulator / GameClock.RealSecondsPerTick;

    /// <summary>Добавление delta секунд кадра; результат - число тиков, которые пора сделать.</summary>
    public int Advance(double delta)
    {
        _accumulator = Math.Min(_accumulator + delta, maxCatchUpTicks * GameClock.RealSecondsPerTick);
        int ticks = 0;
        while (_accumulator >= GameClock.RealSecondsPerTick)
        {
            _accumulator -= GameClock.RealSecondsPerTick;
            ticks++;
        }

        return ticks;
    }
}
