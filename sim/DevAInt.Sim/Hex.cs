using System;
using System.Collections.Generic;

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


    /// <summary>
    /// Метод построения линии до целевого гекса.
    /// </summary>
    /// <param name="other">Целевой гекс</param>
    /// <returns>Путь до целевого гекса.</returns>
    public IEnumerable<Hex> LineTo(Hex other)
    {
        int n = DistanceTo(other);
        for (int i = 0; i <= n; i++)
        {
            float t = n == 0 ? 0f : (float)i / n;
            float q = Q + ((other.Q - Q) * t);
            float r = R + ((other.R - R) * t);
            float s = -q - r;

            int rq = (int)MathF.Round(q);
            int rr = (int)MathF.Round(r);
            int rs = (int)MathF.Round(s);

            float dq = MathF.Abs(rq - q);
            float dr = MathF.Abs(rr - r);
            float ds = MathF.Abs(rs - s);

            if (dq > dr && dq > ds)
            {
                rq = -rr - rs;
            }
            else if (dr > ds)
            {
                rr = -rq - rs;
            }

            yield return new Hex(rq, rr);
        }
    }
}
