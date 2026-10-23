using System;

namespace DevAInt.Sim.Motion;

/// <summary>Ввод движения: направление по x на восток и y на юг, длина 1 - полная скорость.</summary>
public readonly record struct MoveInput(double X, double Y)
{
    /// <summary>Стоять на месте.</summary>
    public static readonly MoveInput None = default;

    /// <summary>То же направление с длиной не больше 1, чтобы по диагонали не ходить быстрее.</summary>
    public MoveInput Clamped()
    {
        double length = Math.Sqrt((X * X) + (Y * Y));
        return length > 1 ? new MoveInput(X / length, Y / length) : this;
    }
}

/// <summary>Параметры тела для движения: радиус круга и скорость ходьбы.</summary>
public sealed record MoveParams(double Radius, double Speed)
{
    /// <summary>Человек: радиус 0.3 м, ходьба 1.5 м/с.</summary>
    public static readonly MoveParams Default = new(0.3, 1.5);
}
