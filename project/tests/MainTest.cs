using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="Main"/> на настоящей сцене main.tscn: запуск цикла, проводка сети роя,
/// кламп кадровой дельты, ввод скорости, teardown с очисткой шины, повторный teardown.
/// </summary>
public class MainTest : CsTestCase
{
    /// <summary>Сцена корня игры.</summary>
    private const string MainScenePath = "res://core/main/main.tscn";

    /// <summary>Полный сброс шины перед каждым тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Сброс шины после теста.</summary>
    public override void AfterEach() => EventBus.Reset();

    /// <summary>Запуск: часы созданы, стартовое время из StartMinutes, оверлей подписан, робот заряжен.</summary>
    public void TestLaunch()
    {
        Main main = SpawnMain();

        CheckTrue(main.Clock is not null, "clock is created on launch");
        CheckEq(main.Clock?.DatetimeStr, "Day 1 08:00", "start time comes from StartMinutes");
        CheckTrue(EventBus.SubscriptionCount > 0, "overlay is subscribed to the bus");
        CheckTrue(main.Robot is not null, "robot is spawned on launch");
        CheckNear(main.Robot?.Battery ?? 0.0f, Robot.BatteryMax, "robot battery is full on launch");
        main.Free();
    }

    /// <summary>Сеть роя создана по границам карты, вышки дают покрытие, слой инициализирован.</summary>
    public void TestSignalGridWiring()
    {
        Main main = SpawnMain();
        SignalGrid? grid = main.SignalGrid;

        CheckTrue(grid is not null, "signal grid is created on launch");
        if (grid is null)
        {
            main.Free();
            return;
        }
        World world = main.GetNode<World>("World");
        CheckEq(grid.Size, world.CellRect.Size, "grid size follows the painted map");
        List<TowerMarker> markers = world.GetTowerMarkers();
        CheckTrue(markers.Count >= 3, "world provides tower markers");
        foreach (TowerMarker marker in markers)
        {
            CheckNear(
                grid.GetCoverage(Iso.WorldToCell(marker.GlobalPosition)), 1.0,
                $"marker tower covers its cell {marker.Name}");
        }
        CheckEq(world.SignalLayer.Polygon.Length, 4, "signal layer is fitted to the grid");
        main.Free();
    }

    /// <summary>Кламп дельты: гигантский кадр впрыскивает не больше MaxFrameDelta.</summary>
    public void TestFrameDeltaClamp()
    {
        Main main = SpawnMain();
        GameClock clock = main.Clock!;

        main._Process(100.0);
        CheckEq(clock.DatetimeStr, "Day 1 08:00", "one huge frame adds no full minute");

        double injected = Main.MaxFrameDelta;
        while (injected < GameClock.MinuteDuration)
        {
            main._Process(100.0);
            injected += Main.MaxFrameDelta;
        }
        CheckEq(clock.DatetimeStr, "Day 1 08:01", "clamped frames accumulate into exactly one minute");
        main.Free();
    }

    /// <summary>Ввод: пауза и смена скорости через input-действия.</summary>
    public void TestTimeInputActions()
    {
        Main main = SpawnMain();
        GameClock clock = main.Clock!;

        main._UnhandledInput(Action("time_pause"));
        CheckEq(clock.Speed, 0, "time_pause sets speed to x0");
        main._UnhandledInput(Action("time_speed_up"));
        CheckEq(clock.Speed, 1, "time_speed_up resumes to x1");
        main._UnhandledInput(Action("time_speed_up"));
        CheckEq(clock.Speed, 2, "time_speed_up raises to x2");
        main._UnhandledInput(Action("time_speed_down"));
        CheckEq(clock.Speed, 1, "time_speed_down lowers back to x1");
        main.Free();
    }

    /// <summary>Teardown: выход из дерева чистит шину и отпускает часы.</summary>
    public void TestTeardownResetsBus()
    {
        Main main = SpawnMain();

        CheckTrue(EventBus.SubscriptionCount > 0, "bus has subscriptions while running");
        main.Free();
        CheckEq(EventBus.SubscriptionCount, 0, "bus is clean after teardown");
    }

    /// <summary>Повторный teardown (WM_CLOSE_REQUEST + выход из дерева) безопасен.</summary>
    public void TestDoubleTeardownIsSafe()
    {
        Main main = SpawnMain();

        main.Notification((int)Node.NotificationWMCloseRequest);
        CheckTrue(main.Clock is null, "close request releases the clock");
        CheckEq(EventBus.SubscriptionCount, 0, "close request cleans the bus");
        main.Free();
        CheckTrue(true, "second teardown on exit does not break");
    }

    /// <summary>Создание Main из сцены с добавлением в корень дерева: вход в дерево запускает цикл.</summary>
    /// <returns>Корень игры; тест освобождает его сам через Free().</returns>
    private static Main SpawnMain()
    {
        Main main = GD.Load<PackedScene>(MainScenePath).Instantiate<Main>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(main);
        return main;
    }

    /// <summary>Событие input-действия в нажатом состоянии.</summary>
    /// <param name="actionName">Имя действия ввода.</param>
    /// <returns>Готовое событие.</returns>
    private static InputEventAction Action(string actionName) =>
        new() { Action = actionName, Pressed = true };
}
