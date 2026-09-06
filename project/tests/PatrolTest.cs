using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="Patrol"/>: позиция из расписания, веер конуса, взгляд по курсу,
/// круговой обзор на стоянке, заморозка на паузе, деинициализация.
/// Патруль инстанцируется из patrol.tscn.
/// </summary>
public class PatrolTest : CsTestCase
{
    /// <summary>Сцена патруля.</summary>
    private const string PatrolScenePath = "res://core/patrol/patrol.tscn";

    /// <summary>Скорость, наземных px за игровую минуту.</summary>
    private const float Speed = 200.0f;

    /// <summary>Стоянка, игровых минут.</summary>
    private const float Dwell = 0.5f;

    /// <summary>Допуск сравнения позиций: геометрия в float.</summary>
    private const double PositionTolerance = 0.001;

    /// <summary>Путевые клетки тестового маршрута.</summary>
    private static readonly Vector2I[] _cells = [new(0, 0), new(4, 0), new(4, 4)];

    /// <summary>Узлы текущего теста - освобождаются в AfterEach.</summary>
    private readonly List<Node> _nodes = [];

    /// <summary>Сброс шины перед тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Освобождение узлов и сброс шины после теста.</summary>
    public override void AfterEach()
    {
        // Ноды не RefCounted: Free() снимает их с дерева и освобождает нативную часть.
        foreach (Node node in _nodes)
        {
            node.Free();
        }
        _nodes.Clear();
        EventBus.Reset();
    }

    /// <summary>Веер конуса построен в _Ready: центр + симметричная дуга.</summary>
    public void TestConeFanIsBuilt()
    {
        Patrol patrol = SpawnPatrol();
        Vector2[] polygon = patrol.GetNode<Polygon2D>("%Cone").Polygon;
        CheckEq(polygon.Length, 14, "fan has origin plus 13 arc points");
        if (polygon.Length != 14)
        {
            return;
        }

        CheckEq(polygon[0], Vector2.Zero, "fan starts at the origin");
        CheckNear(polygon[1].Length(), Patrol.ConeLength, "arc points sit at ConeLength", PositionTolerance);
        CheckNear(polygon[1].Angle(), -Patrol.ConeHalfAngle, "arc starts at -ConeHalfAngle");
        CheckNear(polygon[^1].Angle(), Patrol.ConeHalfAngle, "arc ends at +ConeHalfAngle");
    }

    /// <summary>Позиция узла следует расписанию при движении времени.</summary>
    public void TestPositionFollowsSchedule()
    {
        GameClock clock = new();
        PatrolRecord record = new(MakeRoute());
        Patrol patrol = SpawnPatrol();
        patrol.Init(clock, record);

        patrol._Process(0.0);
        CheckEq(patrol.GlobalPosition, record.SampleAt(0.0f).Position, "position at t = 0");

        double minutes = Dwell + 0.4;
        clock.Advance(minutes * GameClock.MinuteDuration);
        patrol._Process(0.0);
        Vector2 expected = record.SampleAt((float)clock.TimeMinutes).Position;
        CheckNear(patrol.GlobalPosition.X, expected.X, "x follows the schedule", PositionTolerance);
        CheckNear(patrol.GlobalPosition.Y, expected.Y, "y follows the schedule", PositionTolerance);
        patrol.Deinit();
    }

    /// <summary>На ходу конус смотрит по курсу в наземном угле.</summary>
    public void TestConeFacesHeadingWhileMoving()
    {
        GameClock clock = new();
        PatrolRecord record = new(MakeRoute());
        Patrol patrol = SpawnPatrol();
        patrol.Init(clock, record);

        // Середина первого перехода.
        double minutes = Dwell + (Travel(_cells[0], _cells[1]) / 2.0);
        clock.Advance(minutes * GameClock.MinuteDuration);
        patrol._Process(0.0);
        PatrolRecord.Sample sample = record.SampleAt((float)clock.TimeMinutes);
        CheckTrue(!sample.Dwelling, "patrol is moving");
        Vector2 heading = sample.Heading;
        float expected = new Vector2(heading.X, heading.Y / Iso.ScaleY).Angle();
        Node2D pivot = patrol.GetNode<Node2D>("%ConePivot");
        CheckNear(pivot.Rotation, expected, "cone pivot faces the ground angle of heading");
        patrol.Deinit();
    }

    /// <summary>На стоянке конус водит по кругу со временем игры.</summary>
    public void TestConeScansWhileDwelling()
    {
        GameClock clock = new();
        PatrolRecord record = new(MakeRoute());
        Patrol patrol = SpawnPatrol();
        patrol.Init(clock, record);
        Node2D pivot = patrol.GetNode<Node2D>("%ConePivot");

        patrol._Process(0.0);
        float startRotation = pivot.Rotation;
        clock.Advance(0.2 * GameClock.MinuteDuration);
        patrol._Process(0.0);
        CheckTrue(record.SampleAt((float)clock.TimeMinutes).Dwelling, "still dwelling");
        CheckNear(
            pivot.Rotation, Mathf.PosMod(clock.TimeMinutes * Patrol.ScanRate, Mathf.Tau),
            "scan angle is game-time driven");
        CheckTrue(Math.Abs(pivot.Rotation - startRotation) > 0.01f, "the cone actually turns");
        patrol.Deinit();
    }

    /// <summary>Пауза замораживает и позицию, и обзор.</summary>
    public void TestPauseFreezesPatrol()
    {
        GameClock clock = new();
        PatrolRecord record = new(MakeRoute());
        Patrol patrol = SpawnPatrol();
        patrol.Init(clock, record);
        Node2D pivot = patrol.GetNode<Node2D>("%ConePivot");

        clock.Advance((Dwell + 0.3) * GameClock.MinuteDuration);
        patrol._Process(0.0);
        Vector2 position = patrol.GlobalPosition;
        float rotation = pivot.Rotation;

        clock.SetSpeed(0);
        clock.Advance(1000.0);
        patrol._Process(0.0);
        CheckEq(patrol.GlobalPosition, position, "position is frozen on pause");
        CheckNear(pivot.Rotation, rotation, "cone is frozen on pause");
        patrol.Deinit();
    }

    /// <summary>Deinit глушит процессинг и отпускает ссылки.</summary>
    public void TestDeinitStopsProcessing()
    {
        Patrol patrol = SpawnPatrol();
        patrol.Init(new GameClock(), new PatrolRecord(MakeRoute()));
        CheckTrue(patrol.IsProcessing(), "processing after init");
        patrol.Deinit();
        CheckTrue(!patrol.IsProcessing(), "processing stopped after deinit");
    }

    /// <summary>Тестовый замкнутый маршрут по <see cref="_cells"/>.</summary>
    /// <returns>Новый маршрут с копией клеток.</returns>
    private static PatrolRoute MakeRoute() => new([.. _cells], Speed, Dwell, true, 0.0f);

    /// <summary>Длительность перехода между клетками, игровых минут.</summary>
    /// <param name="from">Клетка начала.</param>
    /// <param name="to">Клетка конца.</param>
    /// <returns>Время перехода по скорости <see cref="Speed"/>.</returns>
    private static float Travel(Vector2I from, Vector2I to) =>
        Iso.GroundDistance(Iso.CellToWorld(from), Iso.CellToWorld(to)) / Speed;

    /// <summary>Создание патруля из сцены с добавлением в корень дерева (вход в дерево вызывает _Ready).</summary>
    /// <returns>Патруль, освобождаемый в AfterEach.</returns>
    private Patrol SpawnPatrol()
    {
        Patrol patrol = GD.Load<PackedScene>(PatrolScenePath).Instantiate<Patrol>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(patrol);
        _nodes.Add(patrol);
        return patrol;
    }
}
