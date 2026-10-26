using System;
using System.Linq;
using DevAInt.Sim;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Saves;
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

    /// <summary>Видимость своего персонажа и память тумана войны.</summary>
    private readonly VisionTracker _vision = new();

    /// <summary>Сервер в этом же процессе, null при игре по сети.</summary>
    private GameServer? _server;

    /// <summary>Подключение по сети, null при игре без сети.</summary>
    private EnetClientConnection? _enet;

    /// <summary>Клиент этого процесса.</summary>
    private GameClient? _client;

    /// <summary>Вид мира.</summary>
    private WorldView? _view;

    /// <summary>Камеры и текущий режим.</summary>
    private CameraRig? _cameras;

    /// <summary>Режим камеры при запуске из аргументов.</summary>
    private string? _startCamera;

    /// <summary>HUD своего персонажа.</summary>
    private Hud? _hud;

    /// <summary>Действия управления из данных модов.</summary>
    private GameControls? _controls;

    /// <summary>Таблицы номеров клиента, для обзора своего существа.</summary>
    private ClientTables? _tables;

    /// <summary>Затемнение кадра по видимости.</summary>
    private VisibilityOverlay? _overlay;

    /// <summary>Обзор своего существа или null, если затемнения нет.</summary>
    private VisionDef? _ownVision;

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

    /// <summary>Приём пакетов сервера; неверный пакет или чужие моды - ошибка в лог и false.</summary>
    public static bool TryUpdate(GameClient client, ISimLog log)
    {
        try
        {
            client.Update();
            return true;
        }
        catch (ProtocolException error)
        {
            log.Error(error.Message);
            return false;
        }
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
        GameLocale.Apply(GameLocale.Load(loaded, _log), GameArgs.Language(args));
        ClientTables tables = ClientTables.From(loaded);
        WorldTable world = new(loaded.Defs);
        IConnection? connection = GameArgs.Connect(args) is (string host, int port) ? ConnectTo(host, port) : StartLocalServer(loaded, args);
        if (GameArgs.Player(args) is string player && !SaveGame.IsValidName(player))
        {
            _log.Error($"player id {player} must be 1-64 latin letters, digits, - or _");
            connection = null;
        }

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

        string playerId = GameArgs.Player(args) ?? PlayerIdentity.LoadOrCreate(ProjectSettings.GlobalizePath(PlayerIdentity.DefaultPath));
        _client = new GameClient(connection, tables, playerId);
        GameInput.EnsureActions();
        _controls = new GameControls(tables.Controls, _log);
        _controls.Register();
        _autoWalk = args.Contains("--autowalk");
        _startCamera = GameArgs.Camera(args);
        BuildScene(tables, world);
    }

    /// <summary>Ввод, который не съел интерфейс: зум колёсиком, поворот камеры мышью, смена камеры и выход по Esc.</summary>
    public override void _UnhandledInput(InputEvent input)
    {
        if (input.IsActionPressed(GameInput.Quit))
        {
            GetTree().Quit();
            return;
        }

        if (_cameras is null)
        {
            return;
        }

        if (input.IsActionPressed(GameInput.CameraMode))
        {
            SwitchCamera();
            return;
        }

        if (input is InputEventMouseMotion motion && _cameras.Camera.CapturesMouse)
        {
            _cameras.Camera.Turn(motion.Relative);
            return;
        }

        int steps = GameInput.ZoomSteps(input);
        if (steps != 0)
        {
            _cameras.Camera.Zoom(steps);
        }
    }

    /// <summary>Кадр: сеть, тики по накопленному времени, интерполяция и камера.</summary>
    public override void _Process(double delta)
    {
        if (_client is null || _view is null || _cameras is null || _hud is null)
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
            MoveInput input = ReadInput(_cameras.Camera);
            WorldPos? aim = CursorPoint();
            _client.SendInput(input, aim, _cameras.Camera.FacesAim);
            FaceOwn(input, aim);
            _server?.Tick();
            if (!UpdateClient(_client))
            {
                return;
            }

            if (_client.OwnPosition is WorldPos own)
            {
                _view.MoveOwn(own);
            }

            UpdateVision();
        }

        // По сети пакеты приходят и между тиками: чужие сущности не должны ждать своего тика
        if (!UpdateClient(_client))
        {
            return;
        }

        _controls?.Poll(_client);
        _view.Advance(delta, _timer.Fraction);
        LookOwn();
        FollowOwn(delta);
        _hud.Refresh(delta);
    }

    /// <summary>Запись мира своего сервера и отключение от сетевого при выходе.</summary>
    public override void _ExitTree()
    {
        _server?.Save();
        _enet?.Disconnect();
    }

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
    private IConnection? StartLocalServer(ModLoadResult loaded, string[] args)
    {
        if (WorldStart.Open(loaded, _log, args, ModsPath.SavesRoot()) is not (Simulation sim, SaveGame save))
        {
            return null;
        }

        LoopbackTransport transport = new();
        _server = new GameServer(sim, transport, _log, save);
        return transport.Connect();
    }

    /// <summary>Свет, вид мира, камера и HUD.</summary>
    private void BuildScene(ClientTables tables, WorldTable world)
    {
        _tables = tables;
        AddChild(Lighting.CreateEnvironment());
        AddChild(Lighting.CreateSun());
        _view = new WorldView();
        _view.Init(_client!.Replica, world, tables.Creatures);

        // После смерти своя сущность новая и стоит в точке появления: камера встаёт на неё сразу, без полёта
        _client.Replica.OwnEntityChanged += () => _cameraPlaced = false;
        AddChild(_view);
        _cameras = new CameraRig();
        _cameras.Init(_startCamera);
        AddChild(_cameras);
        ApplyCameraMode();
        _overlay = new VisibilityOverlay();
        _overlay.Init();
        _overlay.Visible = false;
        AddChild(_overlay);

        // Открытая или закрытая дверь меняет, что видно, даже если персонаж стоит
        _client.Replica.EdgeChanged += (_, _) => _vision.Invalidate();
        _hud = new Hud();
        _hud.Init(new HudModel(tables.Stats, tables.Items, _client.Replica));
        AddChild(_hud);
    }

    /// <summary>Видимость своего персонажа раз в тик: затемнение кадра и чужие существа только в видимом.</summary>
    private void UpdateVision()
    {
        Replica replica = _client!.Replica;
        int creature = replica.Entity(replica.OwnEntityId)?.Creature ?? -1;
        VisionDef? vision = creature >= 0 ? _tables!.Creatures.Creature(creature).Vision : null;
        _ownVision = vision;
        if (vision is null || _client.OwnPosition is not WorldPos eye || _view!.Entity(replica.OwnEntityId) is not EntityView own || replica.World is not World world)
        {
            // Существо без обзора видит всё, затемнения нет
            _overlay!.Visible = false;
            _view!.UpdateVisibility(null);
            return;
        }

        // Память тумана копится во всех режимах камеры, чтобы при возврате в изометрию разведанное осталось
        if (_vision.Update(world, eye, (own.Facing.X, own.Facing.Z), vision))
        {
            _overlay!.Refresh(world, _vision.Seen, _vision.Map.Center);
        }

        // Из-за плеча и от первого лица видно то, что видит камера: стены закрывают сами
        bool fog = _cameras!.Camera.UsesFog;
        _overlay!.Visible = fog;
        _view.UpdateVisibility(fog ? _vision.Map : null);
    }

    /// <summary>Глаза для затемнения каждый кадр по плавному положению и взгляду своей капсулы.</summary>
    private void LookOwn()
    {
        if (_overlay!.Visible && _ownVision is not null && _view!.Entity(_client!.Replica.OwnEntityId) is EntityView own)
        {
            _overlay.Look(own.Position, own.Facing, _ownVision);
        }
    }

    /// <summary>Приём пакетов; при ошибке протокола игра выходит, результат false.</summary>
    private bool UpdateClient(GameClient client)
    {
        if (TryUpdate(client, _log))
        {
            return true;
        }

        GetTree().Quit(1);
        _client = null;
        return false;
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
        GameCamera camera = _cameras!.Camera;
        if (_view!.Entity(_client!.Replica.OwnEntityId) is not EntityView own)
        {
            _view.UpdateCutaway(null, camera.Basis.Z);
            return;
        }

        if (!_cameraPlaced)
        {
            camera.Place(own.Position, own.Facing);
            _cameraPlaced = true;
        }

        // После смерти своя капсула новая: вид от первого лица ставится и ей
        own.SetFirstPerson(!camera.ShowsOwnBody);
        camera.Follow(own.Position, _client.Replica.World, _controls?.IsAiming() == true, delta);
        _view.UpdateCutaway(camera.CutsWalls ? own.Position : null, camera.Basis.Z);
    }

    /// <summary>Следующий режим камеры: новая камера встаёт на свою капсулу сразу.</summary>
    private void SwitchCamera()
    {
        EntityView? own = _view?.Entity(_client!.Replica.OwnEntityId);
        _cameras!.Next(own?.Position ?? Vector3.Zero, own?.Facing ?? Vector3.Back);
        ApplyCameraMode();
    }

    /// <summary>Мышь захватывается в режимах, где она крутит камеру; в изометрии она свободна для курсора.</summary>
    private void ApplyCameraMode()
    {
        // Без окна захватывать нечего, а в тестах мышь трогать нельзя
        if (DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = _cameras!.Camera.CapturesMouse ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        }

        _log.Info($"camera {_cameras!.Mode}");
    }

    /// <summary>Точка прицела на земле своего этажа в координатах sim: под курсором в изометрии, по центру кадра в остальных режимах; null без своей сущности.</summary>
    private WorldPos? CursorPoint()
    {
        if (_client?.OwnPosition is not WorldPos own || _cameras is null)
        {
            return null;
        }

        GameCamera camera = _cameras.Camera;
        Viewport viewport = GetViewport();
        Vector2 screen = camera.AimScreenPoint(viewport.GetMousePosition(), viewport.GetVisibleRect().Size);
        Vector3 point = GameInput.AimPoint(camera.ProjectRayOrigin(screen), camera.ProjectRayNormal(screen), own.Z * ViewMath.FloorHeight);
        return new WorldPos(point.X, point.Z, own.Z);
    }

    /// <summary>Взгляд своей капсулы: на точку прицела при прицеле и от первого лица, иначе по вводу движения.</summary>
    private void FaceOwn(MoveInput input, WorldPos? aim)
    {
        bool facesAim = _controls?.IsAiming() == true || _cameras!.Camera.FacesAim;
        if (facesAim && aim is WorldPos point && _client?.OwnPosition is WorldPos own)
        {
            _view!.FaceOwn(new MoveInput(point.X - own.X, point.Y - own.Y));
            return;
        }

        _view!.FaceOwn(input);
    }

    /// <summary>Ввод с клавиатуры или автоходьба по кругу.</summary>
    private MoveInput ReadInput(GameCamera camera)
    {
        if (_autoWalk)
        {
            return new MoveInput(Math.Cos(_time), Math.Sin(_time));
        }

        return GameInput.ToMoveInput(GameInput.ReadKeys(), camera.Basis);
    }
}
