using System.Collections.Generic;
using Godot;

/// <summary>
/// Узел маршрута патруля.
/// Рисуется в редакторе как <see cref="Path2D"/>, точки кривой - путевые метки.
/// В игре превращается в <see cref="PatrolRoute"/>.
/// </summary>
public partial class PatrolPath : Path2D
{
    /// <summary>
    /// Скорость в мировых px за игровую минуту.
    /// </summary>
    [Export]
    public float Speed { get; set; } = 400.0f;

    /// <summary>
    /// Стоянка на каждой путевой точке в игровых минутах. Например "сканирует".
    /// </summary>
    [Export]
    public float DwellMinutes { get; set; } = 0.5f;

    /// <summary>
    /// Флаг, является ли маршрут замкнутым. Если нет, то вернется обратно.
    /// </summary>
    [Export]
    public bool Loop { get; set; } = true;

    /// <summary>
    /// Сдвиг расписания в игровых минутах - чтобы два патруля на одном маршруте не шли строем.
    /// </summary>
    [Export]
    public float StartOffsetMinutes { get; set; } = 0.0f;


    /// <summary>
    /// Метод сборки данных маршрута.
    /// Мировые точки -> клетки, а подряд идущие дубли схлопываются.
    /// </summary>
    /// <returns>Вернет экземпляр <see cref="PatrolRoute"/>.</returns>
    public PatrolRoute BuildRoute()
    {
        List<Vector2I> cells = [];
        for (int idx = 0; idx < Curve.PointCount; idx++)
        {
            Vector2I cell = Iso.WorldToCell(ToGlobal(Curve.GetPointPosition(idx)));
            if (cells.Count == 0 || cells[^1] != cell)
            {
                cells.Add(cell);
            }
        }
        return new(cells, Speed, DwellMinutes, Loop, StartOffsetMinutes);
    }
}
