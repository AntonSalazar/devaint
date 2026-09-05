using System.Linq;
using Godot;

/// <summary>
/// Тесты <see cref="PatrolRecord"/> и <see cref="PatrolPath"/>: период, стоянки,
/// движение по отрезкам, замкнутость, пинг-понг, сдвиг расписания, отсутствие телепортов.
/// </summary>
public class PatrolRecordTest : CsTestCase
{
    /// <summary>Скорость тестового маршрута, наземных px за игровую минуту.</summary>
    private const float Speed = 200.0f;

    /// <summary>Стоянка тестового маршрута, игровых минут.</summary>
    private const float Dwell = 0.5f;

    /// <summary>Допуск сравнения позиций: геометрия в float, позиции до ~500 px.</summary>
    private const double PositionTolerance = 0.001;

    /// <summary>Путевые клетки тестового маршрута.</summary>
    private static readonly Vector2I[] Cells = [new(0, 0), new(4, 0), new(4, 4)];

    /// <summary>Период замкнутого маршрута: сумма стоянок и наземных длин по скорости.</summary>
    public void TestLoopPeriod()
    {
        PatrolRecord record = new(MakeRoute(loop: true));
        CheckEq(record.SegmentCount, Cells.Length, "loop: segment per cell");
        CheckNear(record.Period, LoopPeriod(), "period is dwells plus travels", PositionTolerance);
    }

    /// <summary>В нулевой момент патруль стоит на первой клетке.</summary>
    public void TestSampleAtStart()
    {
        PatrolRecord record = new(MakeRoute(loop: true));
        PatrolRecord.Sample sample = record.SampleAt(0.0f);
        CheckEq(sample.Position, Iso.CellToWorld(Cells[0]), "position is the first cell");
        CheckTrue(sample.Dwelling, "dwelling at start");
        CheckEq(sample.Segment, 0, "segment 0");
    }

    /// <summary>Середина первого перехода: lerp между центрами клеток, не стоянка.</summary>
    public void TestSampleMidSegment()
    {
        PatrolRecord record = new(MakeRoute(loop: true));
        float travel = Travel(Cells[0], Cells[1]);
        PatrolRecord.Sample sample = record.SampleAt(Dwell + (travel / 2.0f));
        Vector2 expected = Iso.CellToWorld(Cells[0]).Lerp(Iso.CellToWorld(Cells[1]), 0.5f);
        CheckNearPosition(sample.Position, expected, "midpoint");
        CheckTrue(!sample.Dwelling, "moving, not dwelling");
    }

    /// <summary>Середина ВТОРОГО перехода: время прошлых отрезков списывается полностью.</summary>
    public void TestSampleSecondSegment()
    {
        PatrolRecord record = new(MakeRoute(loop: true));
        float t = Dwell + Travel(Cells[0], Cells[1]) + Dwell + (Travel(Cells[1], Cells[2]) / 2.0f);
        PatrolRecord.Sample sample = record.SampleAt(t);
        Vector2 expected = Iso.CellToWorld(Cells[1]).Lerp(Iso.CellToWorld(Cells[2]), 0.5f);
        CheckEq(sample.Segment, 1, "second segment");
        CheckNearPosition(sample.Position, expected, "midpoint of segment 2");
    }

    /// <summary>Через период маршрут возвращается в ту же точку.</summary>
    public void TestSampleWrapsByPeriod()
    {
        PatrolRecord record = new(MakeRoute(loop: true));
        foreach (float t in new[] { 0.1f, 1.7f, 3.3f })
        {
            PatrolRecord.Sample a = record.SampleAt(t);
            PatrolRecord.Sample b = record.SampleAt(t + record.Period);
            CheckNearPosition(a.Position, b.Position, $"wraps at t={t:F1}");
        }
    }

    /// <summary>Сдвиг расписания смещает весь маршрут во времени.</summary>
    public void TestStartOffset()
    {
        PatrolRecord baseRecord = new(MakeRoute(loop: true));
        PatrolRecord shifted = new(MakeRoute(loop: true, startOffsetMinutes: 1.25f));
        foreach (float t in new[] { 0.0f, 0.8f, 2.1f })
        {
            PatrolRecord.Sample a = baseRecord.SampleAt(t);
            PatrolRecord.Sample b = shifted.SampleAt(t + 1.25f);
            CheckNearPosition(a.Position, b.Position, $"offset shifts at t={t:F1}");
        }
    }

    /// <summary>Незамкнутый маршрут идет обратно по тем же точкам.</summary>
    public void TestPingPong()
    {
        PatrolRecord record = new(MakeRoute(loop: false));
        CheckEq(record.SegmentCount, (Cells.Length - 1) * 2, "segments there and back");

        // Середина последнего (обратного) отрезка: из Cells[1] в Cells[0].
        float t = record.Period - (Travel(Cells[1], Cells[0]) / 2.0f);
        PatrolRecord.Sample sample = record.SampleAt(t);
        Vector2 expected = Iso.CellToWorld(Cells[1]).Lerp(Iso.CellToWorld(Cells[0]), 0.5f);
        CheckNearPosition(sample.Position, expected, "return leg");
    }

    /// <summary>Патруль не телепортируется: наземный шаг не быстрее скорости.</summary>
    public void TestNoTeleports()
    {
        PatrolRecord record = new(MakeRoute(loop: true));
        const float step = 0.05f;
        int violations = 0;
        Vector2 previous = record.SampleAt(0.0f).Position;
        for (float t = step; t < record.Period * 2.0f; t += step)
        {
            Vector2 current = record.SampleAt(t).Position;
            if (Iso.GroundDistance(previous, current) > (Speed * step) + 0.001f)
            {
                violations++;
            }
            previous = current;
        }
        CheckEq(violations, 0, "ground step never exceeds speed * dt");
    }

    /// <summary>Маршрут из одной клетки - вечная стоянка в ее центре.</summary>
    public void TestSingleCellRoute()
    {
        PatrolRoute route = new([new Vector2I(2, 2)], Speed, Dwell, true, 0.0f);
        PatrolRecord record = new(route);
        CheckTrue(record.Period > 0.0f, "period is positive");
        foreach (float t in new[] { 0.0f, 5.0f, 123.4f })
        {
            PatrolRecord.Sample sample = record.SampleAt(t);
            CheckEq(sample.Position, Iso.CellToWorld(new Vector2I(2, 2)), $"stays at t={t:F1}");
        }
    }

    /// <summary>Пустой маршрут - ошибка без падения, безопасные ответы.</summary>
    public void TestEmptyRouteIsSafe()
    {
        GD.Print("        (an ERROR from PatrolRecord is expected below - part of the test)");
        PatrolRecord record = new(new PatrolRoute([], Speed, Dwell, true, 0.0f));
        CheckTrue(record.Period > 0.0f, "period stays positive");
        CheckTrue(record.SampleAt(1.0f).Dwelling, "sample answers safely");
    }

    /// <summary>PatrolPath собирает маршрут из кривой со схлопыванием дублей.</summary>
    public void TestPatrolPathBuildsRoute()
    {
        // Path2D - нода, а не RefCounted: GC ее не освободит, Free() обязателен.
        // Curve2D - ресурс (RefCounted), его освобождать не нужно.
        PatrolPath path = new()
        {
            Curve = new Curve2D(),
            Speed = 123.0f,
            DwellMinutes = 0.25f,
            Loop = false,
            StartOffsetMinutes = 2.0f,
        };
        try
        {
            path.Curve.AddPoint(Iso.CellToWorld(new Vector2I(0, 0)));
            path.Curve.AddPoint(Iso.CellToWorld(new Vector2I(0, 0)) + new Vector2(4.0f, 0.0f));
            path.Curve.AddPoint(Iso.CellToWorld(new Vector2I(3, 0)));

            PatrolRoute route = path.BuildRoute();
            CheckTrue(
                route.Cells.SequenceEqual([new Vector2I(0, 0), new Vector2I(3, 0)]),
                "cells deduped");
            CheckNear(route.Speed, 123.0, "speed copied");
            CheckNear(route.DwellMinutes, 0.25, "dwell copied");
            CheckTrue(!route.Loop, "loop copied");
            CheckNear(route.StartOffsetMinutes, 2.0, "offset copied");
        }
        finally
        {
            path.Free();
        }
    }

    /// <summary>Тестовый маршрут по <see cref="Cells"/>.</summary>
    /// <param name="loop">Флаг замкнутости.</param>
    /// <param name="startOffsetMinutes">Сдвиг расписания, минут.</param>
    /// <returns>Новый маршрут с копией клеток.</returns>
    private static PatrolRoute MakeRoute(bool loop, float startOffsetMinutes = 0.0f) =>
        new([.. Cells], Speed, Dwell, loop, startOffsetMinutes);

    /// <summary>Длительность перехода между клетками, игровых минут.</summary>
    /// <param name="from">Клетка начала.</param>
    /// <param name="to">Клетка конца.</param>
    /// <returns>Время перехода по скорости <see cref="Speed"/>.</returns>
    private static float Travel(Vector2I from, Vector2I to) =>
        Iso.GroundDistance(Iso.CellToWorld(from), Iso.CellToWorld(to)) / Speed;

    /// <summary>Период замкнутого маршрута по <see cref="Cells"/>.</summary>
    /// <returns>Сумма стоянок и переходов.</returns>
    private static float LoopPeriod()
    {
        float period = Dwell * Cells.Length;
        for (int idx = 0; idx < Cells.Length; idx++)
        {
            period += Travel(Cells[idx], Cells[(idx + 1) % Cells.Length]);
        }
        return period;
    }

    /// <summary>Проверка близости двух позиций с допуском <see cref="PositionTolerance"/>.</summary>
    /// <param name="got">Полученная позиция.</param>
    /// <param name="expected">Ожидаемая позиция.</param>
    /// <param name="what">Описание проверки.</param>
    private void CheckNearPosition(Vector2 got, Vector2 expected, string what)
    {
        CheckNear(got.X, expected.X, $"{what} x", PositionTolerance);
        CheckNear(got.Y, expected.Y, $"{what} y", PositionTolerance);
    }
}
