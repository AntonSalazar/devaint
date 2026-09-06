using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Класс Main игры.
/// Опорная точка, откуда всё начинается и где всё заканчивается.
/// </summary>
public partial class Main : Node
{
    /// <summary>
    /// Время в игровых минутах, с которого начинается игра: 480m -> 8h.
    /// </summary>
    public const int StartMinutes = 480;

    /// <summary>
    /// Максимальная дельта кадра, сек.
    /// На случай лагов игры или накопления дельты со стороны движка.
    /// </summary>
    public const double MaxFrameDelta = 1.0;

    /// <summary>
    /// Префаб робота игрока (res://core/robot/robot.tscn).
    /// </summary>
    private static readonly PackedScene _robotScene = GD.Load<PackedScene>("uid://jwkot7562ai0");

    /// <summary>
    /// Префаб патруля (res://core/patrol/patrol.tscn).
    /// </summary>
    private static readonly PackedScene _patrolScene = GD.Load<PackedScene>("uid://dbnohn5ckil27");

    /// <summary>
    /// Список патрулей роя.
    /// </summary>
    private readonly List<Patrol> _patrols = [];

    /// <summary>
    /// Ссылка на экземпляр вывода отладки.
    /// Заполняется в <see cref="_Ready"/>.
    /// </summary>
    private DebugOverlay _debugOverlay = null!;

    /// <summary>
    /// Ссылка на экземпляр игровых часов. Есть только между Launch и Teardown.
    /// </summary>
    public GameClock? Clock { get; private set; }

    /// <summary>
    /// Ссылка на экземпляр мира. Есть только между Launch и Teardown.
    /// </summary>
    public World? World { get; private set; }

    /// <summary>
    /// Ссылка на экземпляр робота игрока. Есть только между Launch и Teardown.
    /// </summary>
    public Robot? Robot { get; private set; }

    /// <summary>
    /// Ссылка на экземпляр сети роя. Есть только между Launch и Teardown.
    /// </summary>
    public SignalGrid? SignalGrid { get; private set; }

    /// <summary>
    /// Метод, вызываемый при первом кадре: запуск цикла игры.
    /// </summary>
    public override void _Ready()
    {
        SetProcess(false);
        SetProcessUnhandledInput(false);
        _debugOverlay = GetNode<DebugOverlay>("%DebugOverlay");
        Launch();
    }

    /// <summary>
    /// Метод, вызываемый при получении уведомления от движка.
    /// </summary>
    /// <param name="what">Код уведомления.</param>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            Teardown();
        }
    }

    /// <summary>
    /// Метод процессинга: продвижение игровых часов с клампом дельты.
    /// </summary>
    /// <param name="delta">Время между кадрами.</param>
    public override void _Process(double delta) => Clock?.Advance(Math.Min(delta, MaxFrameDelta));

    /// <summary>
    /// Метод, вызываемый при получении необработанного ввода: управление скоростью времени.
    /// </summary>
    /// <param name="event">Событие ввода.</param>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (Clock is null)
        {
            return;
        }

        if (@event.IsActionPressed(InputAction.TimePause))
        {
            Clock.TogglePause();
        }
        else if (@event.IsActionPressed(InputAction.TimeSpeedUp))
        {
            Clock.ChangeSpeed(+1);
        }
        else if (@event.IsActionPressed(InputAction.TimeSpeedDown))
        {
            Clock.ChangeSpeed(-1);
        }
    }

    /// <summary>
    /// Метод, вызываемый при выходе из дерева.
    /// </summary>
    public override void _ExitTree() => Teardown();

    /// <summary>
    /// Метод запуска работы цикла.
    /// </summary>
    private void Launch()
    {
        // Создаем экземпляр таймера.
        GameClock clock = new(StartMinutes);
        Clock = clock;

        // Цепляем мир.
        World world = GetNode<World>("%World");
        World = world;

        // Создаем сеть.
        Rect2I cells = world.CellRect;
        SignalGrid grid = new(cells.Position, cells.Size);
        foreach (TowerMarker marker in world.GetTowerMarkers())
        {
            grid.AddTower(Iso.WorldToCell(marker.GlobalPosition), marker.Radius);
        }
        grid.Init();
        world.SignalLayer.Init(grid);
        SignalGrid = grid;

        // Добавляем игрока.
        Robot robot = _robotScene.Instantiate<Robot>();
        world.AddChild(robot);
        robot.GlobalPosition = world.RobotSpawn;
        robot.Init(clock, grid);
        Robot = robot;

        // Добавляем патрули.
        foreach (PatrolRoute route in world.GetPatrolRoutes())
        {
            Patrol patrol = _patrolScene.Instantiate<Patrol>();
            world.AddChild(patrol);
            patrol.Init(clock, new PatrolRecord(route));
            _patrols.Add(patrol);
        }

        // Добавляем отладочный гуй.
        _debugOverlay.Init(clock, robot);

        // Запускаем процессинг.
        SetProcess(true);
        SetProcessUnhandledInput(true);
    }

    /// <summary>
    /// Метод завершения работы цикла. Повторный вызов безопасен.
    /// </summary>
    private void Teardown()
    {
        // Фильтруем повторное выключение.
        if (Clock is null)
        {
            return;
        }

        GD.Print($"{nameof(Main)}: Teardown... Bye-bye!");

        // Сбрасываем шину.
        EventBus.Reset();

        // Отключаем процессинг.
        SetProcess(false);
        SetProcessUnhandledInput(false);

        // Сбрасываем ссылки.
        _debugOverlay.Deinit();
        World?.SignalLayer.Deinit();
        if (Robot is not null)
        {
            Robot.Deinit();
            Robot.QueueFree();
        }
        SignalGrid?.Deinit();
        foreach (Patrol patrol in _patrols)
        {
            patrol.Deinit();
            patrol.QueueFree();
        }
        _patrols.Clear();

        Robot = null;
        SignalGrid = null;
        World = null;
        Clock = null;
    }

    /// <summary>
    /// Кешированные имена действий ввода управления временем.
    /// </summary>
    private static class InputAction
    {
        /// <summary>Тумблер паузы.</summary>
        public static readonly StringName TimePause = "time_pause";

        /// <summary>Ускорение времени.</summary>
        public static readonly StringName TimeSpeedUp = "time_speed_up";

        /// <summary>Замедление времени.</summary>
        public static readonly StringName TimeSpeedDown = "time_speed_down";
    }
}
