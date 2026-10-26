using System;
using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
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

    /// <summary>Окно инвентаря.</summary>
    private InventoryWindow? _inventory;

    /// <summary>Ближние контейнеры, на которые клиент подписан, по порядку.</summary>
    private List<WorldObject> _nearby = [];

    /// <summary>Меню постройки.</summary>
    private BuildMenu? _buildMenu;

    /// <summary>Призрак постройки под курсором.</summary>
    private BuildGhost? _ghost;

    /// <summary>Что ставится в режиме постановки, null - режима нет.</summary>
    private BuildOption? _building;

    /// <summary>Поворот постройки, 0..3.</summary>
    private int _buildRotation;

    /// <summary>Цель под курсором, если её можно построить, иначе null.</summary>
    private BuildTarget? _buildTarget;

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

        if (input.IsActionPressed(GameInput.Inventory))
        {
            ToggleInventory();
            return;
        }

        if (input.IsActionPressed(GameInput.Build))
        {
            ToggleBuild();
            return;
        }

        if (_building is not null && HandleBuildInput(input))
        {
            return;
        }

        // Пока открыт инвентарь, мышь нужна окну, а не камере
        bool inventoryOpen = _inventory?.Visible == true;
        if (input is InputEventMouseMotion motion && _cameras.Camera.CapturesMouse && !inventoryOpen)
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
            UpdateNearby();
        }

        // По сети пакеты приходят и между тиками: чужие сущности не должны ждать своего тика
        if (!UpdateClient(_client))
        {
            return;
        }

        // В режиме постановки кнопки мыши ставят постройку, а не бьют
        bool overUi = (_inventory?.Visible == true || _buildMenu?.Visible == true) && GetViewport().GuiGetHoveredControl() is not null;
        _controls?.Poll(_client, overUi || _building is not null);
        _view.Advance(delta, _timer.Fraction);
        LookOwn();
        FollowOwn(delta);
        UpdateBuildGhost();
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

        // Открытая или закрытая дверь и поставленный или убранный шкаф меняют, что видно, даже если персонаж стоит
        _client.Replica.EdgeChanged += (_, _) => _vision.Invalidate();
        _client.Replica.ObjectChanged += _ => _vision.Invalidate();
        _hud = new Hud();
        _hud.Init(new HudModel(tables, _client.Replica));
        _hud.ActionPressed += _client.SendUiAction;
        AddChild(_hud);
        _inventory = new InventoryWindow();
        _inventory.Init(tables.Items);
        _inventory.MoveRequested += _client.SendMoveItem;
        _inventory.UseRequested += _client.SendStartAction;
        _inventory.CraftRequested += _client.SendStartCraft;
        AddChild(_inventory);
        _buildMenu = new BuildMenu();
        _buildMenu.Init();
        _buildMenu.Chosen += StartPlacing;
        AddChild(_buildMenu);
        _ghost = new BuildGhost();
        _ghost.Init();
        AddChild(_ghost);
        _client.Replica.InventoryChanged += RefreshBuildMenu;
        _client.Replica.InventoryChanged += RefreshInventory;
        _client.Replica.ContainersChanged += RefreshInventory;
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
        _view.ShowUpTo(camera.HidesUpperFloors ? ViewMath.Level(own.Position.Y) : null);
    }

    /// <summary>Следующий режим камеры: новая камера встаёт на свою капсулу сразу.</summary>
    private void SwitchCamera()
    {
        EntityView? own = _view?.Entity(_client!.Replica.OwnEntityId);
        _cameras!.Next(own?.Position ?? Vector3.Zero, own?.Facing ?? Vector3.Back);
        ApplyCameraMode();
    }

    /// <summary>Мышь захватывается в режимах, где она крутит камеру, если не открыт инвентарь; в изометрии она свободна для курсора.</summary>
    private void ApplyCameraMode()
    {
        // Без окна захватывать нечего, а в тестах мышь трогать нельзя
        if (DisplayServer.GetName() != "headless")
        {
            bool captured = _cameras!.Camera.CapturesMouse && _inventory?.Visible != true && _buildMenu?.Visible != true;
            Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        }

        _log.Info($"camera {_cameras!.Mode}");
    }

    /// <summary>Открытие и закрытие инвентаря: открытое окно подписывается на ближние контейнеры, закрытое - отписывается.</summary>
    private void ToggleInventory()
    {
        _inventory!.Visible = !_inventory.Visible;
        if (_inventory.Visible)
        {
            UpdateNearby();
            RefreshInventory();
        }
        else if (_nearby.Count > 0)
        {
            _nearby = [];
            _client!.SendWatchContainers([]);
        }

        ApplyCameraMode();
    }

    /// <summary>B: в режиме постановки - выход из него, иначе меню постройки открывается или закрывается.</summary>
    private void ToggleBuild()
    {
        if (_building is not null)
        {
            _building = null;
        }
        else
        {
            _buildMenu!.Visible = !_buildMenu.Visible;
            RefreshBuildMenu();
        }

        ApplyCameraMode();
    }

    /// <summary>Пункт меню выбран: меню закрывается, начинается постановка под курсором.</summary>
    private void StartPlacing(BuildOption option)
    {
        _building = option;
        _buildRotation = 0;
        _buildMenu!.Visible = false;
        ApplyCameraMode();
    }

    /// <summary>Ввод режима постановки: левая кнопка ставит, правая отменяет, R поворачивает; true - событие съедено.</summary>
    private bool HandleBuildInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true } click && click.ButtonIndex == MouseButton.Left)
        {
            if (_buildTarget is BuildTarget target)
            {
                _client!.SendBuild(target);
            }

            return true;
        }

        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            _building = null;
            return true;
        }

        if (input.IsActionPressed(GameInput.Rotate))
        {
            _buildRotation = (_buildRotation + 1) % WorldObject.Rotations;
            return true;
        }

        return false;
    }

    /// <summary>Каждый кадр в режиме постановки: цель под курсором, её проверка тем же правилом, что у сервера, и призрак.</summary>
    private void UpdateBuildGhost()
    {
        _buildTarget = null;
        if (_building is not BuildOption option || CursorPoint() is not WorldPos point || _client?.OwnPosition is not WorldPos own || _client.Replica.World is not World world)
        {
            _ghost?.Show(null, false);
            return;
        }

        BuildTarget? target = BuildModel.Target(option, world, point, _buildRotation);
        bool ok = target is not null && option.Ready && BuildModel.Problem(_client.Replica, _tables!, target, own) is null;
        _buildTarget = ok ? target : null;
        _ghost!.Show(target is null ? null : BuildGhost.Box(target, world, _tables!.World), ok);
    }

    /// <summary>Меню постройки заново, если оно открыто, и готовность выбранного пункта по новому инвентарю.</summary>
    private void RefreshBuildMenu()
    {
        if (_client is not GameClient client || _tables is not ClientTables tables)
        {
            return;
        }

        List<BuildOption> options = BuildModel.Options(client.Replica, tables);
        if (_building is BuildOption chosen)
        {
            _building = options.Find(option => option.Kind == chosen.Kind && option.Def == chosen.Def) ?? chosen;
        }

        if (_buildMenu?.Visible == true)
        {
            _buildMenu.Show(options);
        }
    }

    /// <summary>Раз в тик при открытом окне: ближние контейнеры; при смене их набора - новая подписка и окно заново.</summary>
    private void UpdateNearby()
    {
        if (_inventory?.Visible != true || _client?.OwnPosition is not WorldPos own || _client.Replica.World is not World world)
        {
            return;
        }

        List<WorldObject> nearby = InventoryModel.Nearby(world, own);
        if (nearby.SequenceEqual(_nearby))
        {
            return;
        }

        _nearby = nearby;
        _client.SendWatchContainers([.. nearby.Select(obj => obj.Id)]);
        RefreshInventory();
    }

    /// <summary>Окно инвентаря заново по реплике, если оно открыто.</summary>
    private void RefreshInventory()
    {
        if (_inventory?.Visible == true && _client is GameClient client && _tables is ClientTables tables)
        {
            _inventory.Show(InventoryModel.Own(client.Replica, tables), InventoryModel.NearbyPanels(client.Replica, tables, _nearby));
            _inventory.ShowRecipes(InventoryModel.Recipes(client.Replica, tables.Items));
        }
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
