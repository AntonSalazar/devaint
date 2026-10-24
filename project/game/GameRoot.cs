using System;
using System.Linq;
using DevAInt.Sim;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;
using Godot;

/// <summary>Корень игры: клиент с сервером в том же процессе или по сети, тики 10 Гц, вид мира и камера.</summary>
public partial class GameRoot : Node3D
{
    /// <summary>Карта одиночной игры и сервера.</summary>
    public const string MapId = "core:starter_room";

    /// <summary>Сколько секунд ждать подключения к серверу.</summary>
    public const double ConnectTimeoutSeconds = 5;

    /// <summary>Лог.</summary>
    private readonly GodotLog _log = new();

    /// <summary>Счётчик времени до тиков.</summary>
    private readonly TickTimer _timer = new();

    /// <summary>Сервер в этом же процессе, null при игре по сети.</summary>
    private GameServer? _server;

    /// <summary>Подключение по сети, null при игре без сети.</summary>
    private EnetClientConnection? _enet;

    /// <summary>Клиент этого процесса.</summary>
    private GameClient? _client;

    /// <summary>Вид мира.</summary>
    private WorldView? _view;

    /// <summary>Камера.</summary>
    private IsoCamera? _camera;

    /// <summary>HUD своего персонажа.</summary>
    private Hud? _hud;

    /// <summary>Предметы и действия по номерам.</summary>
    private ItemTable? _items;

    /// <summary>Камера уже встала на свою капсулу.</summary>
    private bool _cameraPlaced;

    /// <summary>Подключение по сети хоть раз установилось.</summary>
    private bool _wasConnected;

    /// <summary>Персонаж сам ходит по кругу, для снимков экрана.</summary>
    private bool _autoWalk;

    /// <summary>Время с запуска.</summary>
    private double _time;

    /// <summary>Что не так с подключением к серверу: текст ошибки или null, если всё хорошо.</summary>
    public static string? ConnectionProblem(bool connecting, bool connected, bool wasConnected, double elapsed)
    {
        if (connected)
        {
            return null;
        }

        if (connecting)
        {
            return elapsed > ConnectTimeoutSeconds ? "cannot connect to the server: timed out" : null;
        }

        return wasConnected ? "lost connection to the server" : "cannot connect to the server: refused";
    }

    /// <summary>Загрузка модов, запуск сервера или подключение к нему, постройка сцены.</summary>
    public override void _Ready()
    {
        if (GameMods.Load(_log) is not ModLoadResult loaded)
        {
            GetTree().Quit(1);
            return;
        }

        string[] args = OS.GetCmdlineUserArgs();
        StatTable stats = new(loaded.Defs);
        WorldTable world = new(loaded.Defs);
        _items = new ItemTable(loaded.Defs);
        IConnection? connection = GameArgs.Connect(args) is (string host, int port) ? ConnectTo(host, port) : StartLocalServer(loaded, args);
        if (connection is null)
        {
            GetTree().Quit(1);
            return;
        }

        // Задержка делится поровну на две стороны, чтобы путь туда-обратно занимал ровно --lag
        double lag = GameArgs.LagMs(args);
        if (lag > 0)
        {
            connection = new LaggedConnection(connection, lag / 2000, () => Time.GetTicksUsec() / 1e6);
            _log.Info($"artificial lag {lag} ms round trip");
        }

        _client = new GameClient(connection, stats, _items);
        GameInput.EnsureActions();
        _autoWalk = args.Contains("--autowalk");
        BuildScene(stats, world);
    }

    /// <summary>Ввод, который не съел интерфейс: зум колёсиком и выход по Esc.</summary>
    public override void _UnhandledInput(InputEvent input)
    {
        if (input.IsActionPressed(GameInput.Quit))
        {
            GetTree().Quit();
            return;
        }

        int steps = GameInput.ZoomSteps(input);
        if (steps != 0)
        {
            _camera?.Zoom(steps);
        }
    }

    /// <summary>Кадр: сеть, тики по накопленному времени, интерполяция и камера.</summary>
    public override void _Process(double delta)
    {
        if (_client is null || _view is null || _camera is null || _hud is null)
        {
            return;
        }

        _time += delta;
        _enet?.Poll();
        if (!CheckConnection())
        {
            return;
        }

        for (int ticks = _timer.Advance(delta); ticks > 0; ticks--)
        {
            _client.SendInput(ReadInput(_camera));
            _server?.Tick();
            _client.Update();
            if (_client.OwnPosition is WorldPos own)
            {
                _view.MoveOwn(own);
            }
        }

        // По сети пакеты приходят и между тиками: чужие сущности не должны ждать своего тика
        _client.Update();
        UseItemOnKey();
        _view.Advance(delta, _timer.Fraction);
        FollowOwn(delta);
        _hud.Refresh();
    }

    /// <summary>Отключение от сервера при выходе.</summary>
    public override void _ExitTree() => _enet?.Disconnect();

    /// <summary>Подключение к серверу по сети или null, если пир не создался.</summary>
    private EnetClientConnection? ConnectTo(string host, int port)
    {
        try
        {
            _enet = new EnetClientConnection(host, port);
            _log.Info($"connecting to {host}:{port}");
            return _enet;
        }
        catch (InvalidOperationException error)
        {
            _log.Error(error.Message);
            return null;
        }
    }

    /// <summary>Сервер в этом же процессе и подключение к нему через loopback.</summary>
    private IConnection StartLocalServer(ModLoadResult loaded, string[] args)
    {
        Simulation sim = new(loaded, _log, MapId, new GameClock(GameArgs.TimeScale(args)));
        foreach (ModError error in sim.ScriptErrors)
        {
            _log.Error(error.ToString());
        }

        LoopbackTransport transport = new();
        _server = new GameServer(sim, transport, _log);
        return transport.Connect();
    }

    /// <summary>Свет, вид мира, камера и HUD.</summary>
    private void BuildScene(StatTable stats, WorldTable world)
    {
        AddChild(Lighting.CreateEnvironment());
        AddChild(Lighting.CreateSun());
        _view = new WorldView();
        _view.Init(_client!.Replica, world);
        AddChild(_view);
        _camera = new IsoCamera();
        _camera.Init(Vector3.Zero);
        AddChild(_camera);
        _hud = new Hud();
        _hud.Init(new HudModel(stats, _items!, _client.Replica));
        AddChild(_hud);
    }

    /// <summary>Проверка подключения по сети; при ошибке игра выходит, результат false.</summary>
    private bool CheckConnection()
    {
        if (_enet is null)
        {
            return true;
        }

        _wasConnected |= _enet.IsConnected;
        string? problem = ConnectionProblem(_enet.IsConnecting, _enet.IsConnected, _wasConnected, _time);
        if (problem is null)
        {
            return true;
        }

        _log.Error(problem);
        GetTree().Quit(_wasConnected ? 0 : 1);
        _enet = null;
        _client = null;
        return false;
    }

    /// <summary>Камера и срез стен идут за своей капсулой; при первом появлении камера встаёт на неё сразу.</summary>
    private void FollowOwn(double delta)
    {
        if (_view!.Entity(_client!.Replica.OwnEntityId) is not EntityView own)
        {
            Cutaway.Disable();
            return;
        }

        if (!_cameraPlaced)
        {
            _camera!.SnapTo(own.Position);
            _cameraPlaced = true;
        }

        _camera!.Follow(own.Position, delta);
        Cutaway.SetFocus(own.Position);
    }

    /// <summary>По клавише E - первое применение первого подходящего предмета, если сейчас нет действия.</summary>
    private void UseItemOnKey()
    {
        if (!Input.IsActionJustPressed(GameInput.Use) || _client is null || _items is null)
        {
            return;
        }

        Replica replica = _client.Replica;
        bool busy = replica.Entity(replica.OwnEntityId)?.Action is not null;
        if (!busy && GameInput.FirstUse(replica, _items) is (long item, int action))
        {
            _client.SendStartAction(item, action);
        }
    }

    /// <summary>Ввод с клавиатуры или автоходьба по кругу.</summary>
    private MoveInput ReadInput(IsoCamera camera)
    {
        if (_autoWalk)
        {
            return new MoveInput(Math.Cos(_time), Math.Sin(_time));
        }

        return GameInput.ToMoveInput(GameInput.ReadKeys(), camera.Basis);
    }
}
