using System.Collections.Generic;
using Godot;

/// <summary> Запись патруля.
/// Берет маршрут <see cref="PatrolRoute"/>, предвычисляет отрезки и отвечает на вопрос
/// "где патруль в момент T". Не тикает, и не знает о сцене - это тёплый агент
/// из 09-ARCHITECTURE.md. Активная сцена просто ставит себя в
/// [code]Sample(clock.GetTimeMinutes()).Position[/code].
/// Длины отрезков и скорость - в наземных px <see cref="Iso.GroundDistance"/>,
/// чтобы темп патруля был сравним с роботом <see cref="Robot"/> не зависимо от направления.
/// </summary>
public class PatrolRecord
{
    /// <summary>Предвычисленные отрезки в порядке обхода.</summary>
    private readonly List<Segment> _segments = [];

    ///<summary>Ссылка на экземпляр маршрута.</summary>
    public PatrolRoute Route { get; }

    /// <summary>Период расписания в игровых минутах.</summary>
    public float Period { get; private set; } = 1.0f;

    /// <summary>Число отрезков на маршруте.</summary>
    public int SegmentCount => _segments.Count;


    /// <summary>Конструктор.</summary>
    public PatrolRecord(PatrolRoute route)
    {
        Route = route;
        if (route.Cells.Count == 0)
        {
            GD.PushError($"{this}: empty route detected!");
            Route = new([], 0.0f, 0.0f, true, 0.0f);
            return;
        }
        Build();
    }

    /// <summary>Метод снимка патруля в момент игрового времени.</summary>
    public Sample SampleAt(float timeMinutes)
    {
        if (SegmentCount == 0)
        {
            return new(Vector2.Zero, 0, true, Vector2.Zero);
        }

        float t = Mathf.PosMod(timeMinutes - Route.StartOffsetMinutes, Period);
        for (int idx = 0; idx < SegmentCount; idx++)
        {
            Segment segment = _segments[idx];

            // Стоянка в начале отрезка.
            if (t < Route.DwellMinutes)
            {
                return new(segment.From, idx, true, segment.Heading);
            }
            t -= Route.DwellMinutes;

            // Движение по отрезку.
            if (t < segment.Travel)
            {
                Vector2 position = segment.From.Lerp(segment.To, t / segment.Travel);
                return new(position, idx, false, segment.Heading);
            }
            t -= segment.Travel;
        }

        // Численный хвост периода - возвращаемся в начало.
        return new(_segments[0].From, 0, true, _segments[0].Heading);
    }


    /// <summary>Метод создания отрезка с from до to с длительностью по скорости.</summary>
    private Segment MakeSegment(Vector2 from, Vector2 to)
    {
        float travel = 0.0f;
        if (Route.Speed > 0.0f)
        {
            travel = Iso.GroundDistance(from, to) / Route.Speed;
        }
        return new(from, to, travel);
    }


    /// <summary>Метод предвычисления отрезков и периода по маршруту.</summary>
    private void Build()
    {
        _segments.Clear();
        Vector2[] points = new Vector2[Route.Cells.Count];
        for (int idx = 0; idx < points.Length; idx++)
        {
            points[idx] = Iso.CellToWorld(Route.Cells[idx]);
        }

        // Одна клетка - стоянка на месте.
        if (points.Length == 1)
        {
            _segments.Add(new(points[0], points[0], 0.0f));
        }
        else
        {
            // Туда.
            for (int idx = 0; idx < points.Length - 1; idx++)
            {
                _segments.Add(MakeSegment(points[idx], points[idx + 1]));
            }

            // Замыкание или обратно по тем же точкам.
            if (Route.Loop)
            {
                _segments.Add(MakeSegment(points[^1], points[0]));
            }
            else
            {
                for (int idx = points.Length - 1; idx > 0; idx--)
                {
                    _segments.Add(MakeSegment(points[idx], points[idx - 1]));
                }
            }
        }

        // Период: каждый отрезок = стоянка + движение.
        Period = 0.0f;
        foreach (Segment segment in _segments)
        {
            Period += Route.DwellMinutes + segment.Travel;
        }
        if (Period <= 0.0f)
        {
            Period = 1.0f;
        }
    }


    /// <summary>Отрезок маршрута.</summary>
    /// <param name="from">Откуда начинается отрезок.</param>
    /// <param name="to">Где заканчивается отрезок.</param>
    /// <param name="travel">Длительность движения в игровых минутах (длина / speed).</param>
    public class Segment(Vector2 from, Vector2 to, float travel)
    {
        /// <summary>Откуда начинается отрезок.</summary>
        public Vector2 From { get; } = from;

        /// <summary>Где заканчивается отрезок.</summary>
        public Vector2 To { get; } = to;

        /// <summary>Длительность движения в игровых минутах (длина / speed).</summary>
        public float Travel { get; } = travel;

        /// <summary>Направление движения (нулевое для отрезка нулевой длины).</summary>
        public Vector2 Heading { get; } = (to - from).Normalized();
    }

    /// <summary>Снимок патруля в момент времени.</summary>
    /// <param name="position">Мировая позиция в координатах.</param>
    /// <param name="segment">Индекс текущего сегмента.</param>
    /// <param name="dwelling">Флаг стоянки на путевой точке.</param>
    /// <param name="heading">Направление движения/взгляда.</param>
    public class Sample(Vector2 position, int segment, bool dwelling, Vector2 heading)
    {
        /// <summary>Мировая позиция в px.</summary>
        public Vector2 Position { get; } = position;

        /// <summary>Индекс текущего сегмента.</summary>
        public int Segment { get; } = segment;

        /// <summary>Флаг стоянки на путевой точке.</summary>
        public bool Dwelling { get; } = dwelling;

        /// <summary>Направление движения/взгляда.</summary>
        public Vector2 Heading { get; } = heading;

    }
}
