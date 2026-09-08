using System;

namespace DevAInt.Sim;


/// <summary>
/// Структура гекса в аксиальных координатах.
/// </summary>
/// <param name="Q">Номер косого столбца.</param>
/// <param name="R">Номер ряда.</param>
public readonly record struct Hex(int Q, int R)
{
    /// <summary>
    /// Направления-константы, против часовой стрелки.
    /// </summary>
    public static readonly Hex[] Directions =
    [
        new(1, 0),  // Вправо.
        new(1, -1), // Вверх-вправо.
        new(0, -1), // Вверх-влево.
        new(-1, 0), // Влево.
        new(-1, 1), // Вниз-влево.
        new(0, 1),  // Вниз-вправо.
    ];


    /// <summary>
    /// Метод возврата структуры соседней клетки.
    /// </summary>
    /// <param name="dir">Индекс направления.</param>
    /// <returns>Вернет соседнюю клетку относительно направления.</returns>
    public Hex Neighbor(int dir)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dir);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dir, Directions.Length);
        Hex d = Directions[dir];
        return new Hex(Q + d.Q, R + d.R);
    }


    /// <summary>
    /// Метод возврата дистанции до целевой клетки.
    /// </summary>
    /// <param name="other">Целевая клетка.</param>
    /// <returns>Дистанция в количестве клеток.</returns>
    public int DistanceTo(Hex other)
    {
        int dq = Math.Abs(Q - other.Q);
        int dr = Math.Abs(R - other.R);
        int ds = Math.Abs(-Q - R - (-other.Q - other.R));
        return Math.Max(dq, Math.Max(dr, ds));
    }


    /// <summary>
    /// Метод возврата представления клетки в виде столбца и строки.
    /// </summary>
    /// <returns>Вернет кортеж столбца и строки.</returns>
    public (int Col, int Row) ToOffset() =>
        (Q + ((R - (R & 1)) / 2), R);


    /// <summary>
    /// Метод возврата экземпляра клетки.
    /// </summary>
    /// <param name="col">Номер столбца.</param>
    /// <param name="row">Номер строки.</param>
    /// <returns>Вернет новый экземпляр клетки.</returns>
    public static Hex FromOffset(int col, int row) =>
        new(col - ((row - (row & 1)) / 2), row);
}
