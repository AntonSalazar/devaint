using System.Collections.Generic;
using Godot;

/// <summary>Контейнер данных о маршруте.</summary>
/// <param name="cells">Ячейки путей.</param>
/// <param name="speed">Скорость, мировых px за игровую минуту.</param>
/// <param name="dwellMinutes">Стоянка на каждой путевой точке в игровых минутах.</param>
/// <param name="loop">Флаг, является ли маршрут замкнутым. Если нет, то вернется обратно.</param>
/// <param name="startOffsetMinutes">Сдвиг расписания в игровых минутах - чтоб два патруля на одном маршруте не шли строем.</param>
public class PatrolRoute(
    List<Vector2I> cells, float speed,
    float dwellMinutes, bool loop,
    float startOffsetMinutes)
{
    /// <summary>Ячейки путей.</summary>
    public List<Vector2I> Cells { get; } = cells;

    /// <summary>Скорость, мировых px за игровую минуту.</summary>
    public float Speed { get; } = speed;

    /// <summary>Стоянка на каждой путевой точке в игровых минутах (например "сканирует").</summary>
    public float DwellMinutes { get; } = dwellMinutes;

    /// <summary>Флаг, является ли маршрут замкнутым. Если нет, то вернется обратно.</summary>
    public bool Loop { get; } = loop;

    /// <summary>Сдвиг расписания в игровых минутах - чтобы два патруля на одном маршруте не шли строем.</summary>
    public float StartOffsetMinutes { get; } = startOffsetMinutes;

}

