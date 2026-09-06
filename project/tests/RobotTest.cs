using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="Robot"/>: расход батареи по долям состояний, кламп,
/// полнота таблиц, битовая маска состояний и излучение, ореол, физический кадр.
/// Сцена robot.tscn держит GDScript-версию, поэтому дерево собирается руками,
/// а ввод имитируется через Input.ActionPress.
/// </summary>
public class RobotTest : CsTestCase
{
    /// <summary>Шейдер ореола из проекта.</summary>
    private const string HaloShaderPath = "res://core/halo/halo.gdshader";

    /// <summary>Действие ввода для ходьбы в тестах.</summary>
    private const string WalkAction = "move_right";

    /// <summary>Допуск для батареи: проценты во float около 100.</summary>
    private const double BatteryTolerance = 0.0001;

    /// <summary>Узлы текущего теста - освобождаются в AfterEach.</summary>
    private readonly List<Node> _nodes = [];

    /// <summary>Полный сброс шины перед каждым тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Отпускание ввода, освобождение узлов и сброс шины после теста.</summary>
    public override void AfterEach()
    {
        Input.ActionRelease(WalkAction);
        foreach (Node node in _nodes)
        {
            node.Free();
        }
        _nodes.Clear();
        EventBus.Reset();
    }

    /// <summary>Инициализация заряжает батарею до максимума.</summary>
    public void TestInitFillsBattery()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        CheckNear(robot.Battery, Robot.BatteryMax, "init fills the battery");
        robot.Deinit();
    }

    /// <summary>Без накопленных секунд минутный тик не тратит заряд.</summary>
    public void TestNoObservedTimeNoDrain()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        PushMinute();
        CheckNear(robot.Battery, Robot.BatteryMax, "no seconds - no drain");
        robot.Deinit();
    }

    /// <summary>Минута чистого простоя стоит DrainOf(Idle).</summary>
    public void TestIdleMinuteDrain()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        robot._PhysicsProcess(2.5);
        PushMinute();
        CheckNear(
            robot.Battery, Robot.BatteryMax - Robot.DrainOf(Robot.State.Idle),
            "idle minute costs DrainOf(Idle)", BatteryTolerance);
        robot.Deinit();
    }

    /// <summary>Минута чистой ходьбы стоит DrainOf(Walk).</summary>
    public void TestWalkMinuteDrain()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        Input.ActionPress(WalkAction);
        robot._PhysicsProcess(2.5);
        Input.ActionRelease(WalkAction);
        CheckTrue(robot.HasFlag(Robot.State.Walk), "input makes the robot walk");
        PushMinute();
        CheckNear(
            robot.Battery, Robot.BatteryMax - Robot.DrainOf(Robot.State.Walk),
            "walk minute costs DrainOf(Walk)", BatteryTolerance);
        robot.Deinit();
    }

    /// <summary>Смешанная минута тратит взвешенное среднее по долям состояний.</summary>
    public void TestMixedMinuteDrain()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        robot._PhysicsProcess(1.25);
        Input.ActionPress(WalkAction);
        robot._PhysicsProcess(1.25);
        Input.ActionRelease(WalkAction);
        PushMinute();
        float expected = Robot.BatteryMax
            - (Robot.DrainOf(Robot.State.Idle) * 0.5f)
            - (Robot.DrainOf(Robot.State.Walk) * 0.5f);
        CheckNear(robot.Battery, expected, "mixed minute drains a weighted average", BatteryTolerance);
        robot.Deinit();
    }

    /// <summary>Счетчики секунд очищаются после тика.</summary>
    public void TestSecondsResetAfterTick()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        robot._PhysicsProcess(2.5);
        PushMinute();
        float afterFirst = robot.Battery;
        PushMinute();
        CheckNear(robot.Battery, afterFirst, "second tick without seconds is free");
        robot.Deinit();
    }

    /// <summary>Заряд не уходит ниже нуля.</summary>
    public void TestBatteryClampsAtZero()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        Input.ActionPress(WalkAction);
        int minutes = (int)(Robot.BatteryMax / Robot.DrainOf(Robot.State.Walk)) + 5;
        for (int idx = 0; idx < minutes; idx++)
        {
            robot._PhysicsProcess(2.5);
            PushMinute();
        }
        Input.ActionRelease(WalkAction);
        CheckNear(robot.Battery, 0.0, "battery clamps at zero");
        robot.Deinit();
    }

    /// <summary>Каждому состоянию заданы расход и излучение.</summary>
    public void TestTablesAreComplete()
    {
        foreach (Robot.State state in Enum.GetValues<Robot.State>())
        {
            CheckTrue(Robot.DrainOf(state) > 0.0f, $"DrainOf has an entry for {state}");
            CheckTrue(Robot.EmissionOf(state) > 0.0f, $"EmissionOf has an entry for {state}");
        }
    }

    /// <summary>После Init: маска Idle, излучение Idle, ореол построен.</summary>
    public void TestInitialStateAndHalo()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        CheckEq(robot.Flags, Robot.State.Idle, "initial flags are Idle");
        CheckTrue(robot.HasFlag(Robot.State.Idle), "HasFlag(Idle)");
        CheckTrue(!robot.HasFlag(Robot.State.Walk), "not HasFlag(Walk)");
        CheckNear(robot.Emission, Robot.EmissionOf(Robot.State.Idle), "idle emission");
        Halo halo = robot.GetNode<Halo>("%Halo");
        CheckEq(halo.Polygon.Length, 4, "halo quad is built right after init");
        robot.Deinit();
    }

    /// <summary>Излучение суммируется по поднятым битам маски.</summary>
    public void TestEmissionSumsOverFlags()
    {
        Robot robot = SpawnRobot();

        robot.Init(new GameClock(), MakeGrid());
        robot.Flags = Robot.State.Walk;
        CheckNear(robot.Emission, Robot.EmissionOf(Robot.State.Walk), "walk emission");
        robot.Flags = Robot.State.Idle | Robot.State.Walk;
        CheckNear(
            robot.Emission,
            Robot.EmissionOf(Robot.State.Idle) + Robot.EmissionOf(Robot.State.Walk),
            "combined flags sum their emission");
        robot.Deinit();
    }

    /// <summary>Смена маски обновляет ореол; та же маска - нет.</summary>
    public void TestFlagsDriveHalo()
    {
        Robot robot = SpawnRobot();
        robot.Init(new GameClock(), MakeGrid());
        Halo halo = robot.GetNode<Halo>("%Halo");
        ShaderMaterial shader = (ShaderMaterial)halo.Material;

        robot.Flags = Robot.State.Walk;
        halo._Process(1.0);
        float walkRadius = shader.GetShaderParameter("radius").AsSingle();
        CheckNear(
            walkRadius,
            Halo.BaseRadius + (Halo.RadiusPerEmission * Robot.EmissionOf(Robot.State.Walk)),
            "walk radius follows emission");
        robot.Flags = Robot.State.Idle;
        halo._Process(1.0);
        CheckTrue(
            shader.GetShaderParameter("radius").AsSingle() < walkRadius,
            "idle halo is smaller than walk halo");
        robot.Deinit();
    }

    /// <summary>
    /// Физический кадр без ввода: локомоция Idle, чужие биты маски сохраняются,
    /// секунды копятся по Idle.
    /// </summary>
    public void TestPhysicsFrameIdleKeepsModifierBits()
    {
        Robot robot = SpawnRobot();
        robot.Init(new GameClock(), MakeGrid());
        Robot.State modifier = (Robot.State)0x10;

        robot.Flags = Robot.State.Walk | modifier;
        robot._PhysicsProcess(0.1);
        CheckTrue(robot.HasFlag(Robot.State.Idle), "no input -> Idle locomotion");
        CheckTrue(!robot.HasFlag(Robot.State.Walk), "Walk bit is cleared");
        CheckTrue((robot.Flags & modifier) != 0, "modifier bit survives locomotion update");

        // Только Idle-секунды: минута стоит ровно DrainOf(Idle).
        PushMinute();
        CheckNear(
            robot.Battery, Robot.BatteryMax - Robot.DrainOf(Robot.State.Idle),
            "only idle seconds were accumulated", BatteryTolerance);
        robot.Deinit();
    }

    /// <summary>На паузе физический кадр ничего не копит и обнуляет скорость.</summary>
    public void TestPhysicsFrameOnPauseIsInert()
    {
        Robot robot = SpawnRobot();
        GameClock clock = new();
        robot.Init(clock, MakeGrid());

        Input.ActionPress(WalkAction);
        robot._PhysicsProcess(0.1);
        CheckTrue(!robot.Velocity.IsZeroApprox(), "walking before the pause");
        clock.SetSpeed(0);
        robot._PhysicsProcess(0.1);
        Input.ActionRelease(WalkAction);
        CheckTrue(robot.Velocity.IsZeroApprox(), "velocity is zeroed on pause");
        PushMinute();
        CheckNear(
            robot.Battery, Robot.BatteryMax - Robot.DrainOf(Robot.State.Walk),
            "paused frame accumulated nothing", BatteryTolerance);
        robot.Deinit();
    }

    /// <summary>Физический кадр вписывает след в память роя: излучение x покрытие x минуты.</summary>
    public void TestPhysicsFrameAccumulatesNotice()
    {
        Robot robot = SpawnRobot();
        SignalGrid grid = MakeGrid();
        robot.Init(new GameClock(), grid);
        robot.GlobalPosition = Iso.CellToWorld(Vector2I.Zero);

        robot._PhysicsProcess(0.1);
        double expected = Robot.EmissionOf(Robot.State.Idle) * SignalGrid.NoticeRate
            * 0.1 / GameClock.MinuteDuration;
        CheckNear(grid.GetNotice(Vector2I.Zero), expected, "one idle frame at full coverage");
        CheckNear(robot.Notice, expected, "robot reads its own sector notice");
        robot.Deinit();
    }

    /// <summary>На паузе след не пишется.</summary>
    public void TestPhysicsFrameOnPauseAccumulatesNothing()
    {
        Robot robot = SpawnRobot();
        SignalGrid grid = MakeGrid();
        GameClock clock = new();
        robot.Init(clock, grid);
        robot.GlobalPosition = Iso.CellToWorld(Vector2I.Zero);

        clock.SetSpeed(0);
        robot._PhysicsProcess(0.1);
        CheckEq(grid.GetNotices().Count, 0, "no notice while paused");
        robot.Deinit();
    }

    /// <summary>Deinit глушит физику, обнуляет заряд и отписывается от шины.</summary>
    public void TestDeinitStopsEverything()
    {
        Robot robot = SpawnRobot();
        robot.Init(new GameClock(), MakeGrid());
        CheckTrue(robot.IsPhysicsProcessing(), "physics runs after Init");

        robot._PhysicsProcess(2.5);
        robot.Deinit();
        CheckTrue(!robot.IsPhysicsProcessing(), "physics stopped after Deinit");
        CheckNear(robot.Battery, 0.0, "battery is zeroed by Deinit");
        PushMinute();
        CheckNear(robot.Battery, 0.0, "minute tick after Deinit changes nothing");
    }

    /// <summary>Сеть роя для тестов: вышка в (0,0), полное покрытие в ее клетке.</summary>
    /// <returns>Грид с одной вышкой.</returns>
    private static SignalGrid MakeGrid()
    {
        SignalGrid grid = new(new Vector2I(-10, -10), new Vector2I(21, 21));
        grid.AddTower(Vector2I.Zero, 300.0f);
        return grid;
    }

    /// <summary>Публикация минутного тика с валидным снимком времени.</summary>
    private static void PushMinute() =>
        new GameClock.OnMinutePassed(1, new GameClock.GameTime(Day: 1, Hour: 0, Minute: 1)).Push();

    /// <summary>Сборка робота с ореолом руками (как в robot.tscn) и добавление в корень дерева.</summary>
    /// <returns>Робот, освобождаемый в AfterEach.</returns>
    private Robot SpawnRobot()
    {
        Robot robot = new();
        Halo halo = new()
        {
            Name = "Halo",
            Material = new ShaderMaterial { Shader = GD.Load<Shader>(HaloShaderPath) },
        };
        robot.AddChild(halo);
        halo.Owner = robot;
        halo.UniqueNameInOwner = true;

        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(robot);
        _nodes.Add(robot);
        return robot;
    }
}
