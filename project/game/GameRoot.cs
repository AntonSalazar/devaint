using System;
using System.Globalization;
using System.Linq;
using DevAInt.Sim;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;
using Godot;

/// <summary>Корень игры: сервер и клиент в одном процессе через loopback, тики 10 Гц, вид мира и камера.</summary>
public partial class GameRoot : Node3D
{
    /// <summary>Карта одиночной игры.</summary>
    public const string MapId = "core:starter_room";

    /// <summary>Наибольшее число тиков догона за кадр, чтобы после зависания не тикать без конца.</summary>
    private const int MaxCatchUpTicks = 5;

    /// <summary>Моды одиночной игры.</summary>
    private static readonly string[] _mods = ["core"];

    /// <summary>Сервер в этом же процессе.</summary>
    private GameServer? _server;

    /// <summary>Клиент этого процесса.</summary>
    private GameClient? _client;

    /// <summary>Вид мира.</summary>
    private WorldView? _view;

    /// <summary>Камера.</summary>
    private IsoCamera? _camera;

    /// <summary>HUD своего персонажа.</summary>
    private Hud? _hud;

    /// <summary>Накопленное время до следующего тика.</summary>
    private double _accumulator;

    /// <summary>Персонаж сам ходит по кругу, для снимков экрана.</summary>
    private bool _autoWalk;

    /// <summary>Время с запуска, для автоходьбы.</summary>
    private double _time;

    /// <summary>Ускорение игрового времени из аргумента --time-scale N, по умолчанию 1.</summary>
    public static double TimeScale(string[] args)
    {
        int index = Array.IndexOf(args, "--time-scale");
        if (index < 0 || index + 1 >= args.Length)
        {
            return 1;
        }

        bool parsed = double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double scale);
        return parsed && scale > 0 ? scale : 1;
    }

    /// <summary>Загрузка модов, запуск сервера и клиента, постройка сцены.</summary>
    public override void _Ready()
    {
        GodotLog log = new();
        ModLoadResult loaded = ModLoader.Load(ModsPath.Find(), _mods);
        if (!loaded.Ok)
        {
            foreach (ModError error in loaded.Errors)
            {
                log.Error(error.ToString());
            }

            GetTree().Quit(1);
            return;
        }

        string[] args = OS.GetCmdlineUserArgs();
        GameClock clock = new(TimeScale(args));
        Simulation sim = new(loaded, log, MapId, clock);
        foreach (ModError error in sim.ScriptErrors)
        {
            log.Error(error.ToString());
        }

        LoopbackTransport transport = new();
        _server = new GameServer(sim, transport, log);
        _client = new GameClient(transport.Connect(), sim.StatTable);
        GameInput.EnsureActions();
        _autoWalk = args.Contains("--autowalk");

        AddChild(Lighting.CreateEnvironment());
        AddChild(Lighting.CreateSun());
        _view = new WorldView();
        _view.Init(_client.Replica, sim.WorldTable);
        AddChild(_view);
        _camera = new IsoCamera();
        _camera.Init(ViewMath.ToGodot((float)sim.Spawn.X, (float)sim.Spawn.Y, sim.Spawn.Z));
        AddChild(_camera);
        _hud = new Hud();
        _hud.Init(new HudModel(sim.StatTable, _client.Replica));
        AddChild(_hud);
    }

    /// <summary>Кадр: тики по накопленному времени, интерполяция и камера.</summary>
    public override void _Process(double delta)
    {
        if (_server is null || _client is null || _view is null || _camera is null || _hud is null)
        {
            return;
        }

        _time += delta;
        _accumulator = Math.Min(_accumulator + delta, MaxCatchUpTicks * GameClock.RealSecondsPerTick);
        while (_accumulator >= GameClock.RealSecondsPerTick)
        {
            _client.SendInput(ReadInput(_camera));
            _server.Tick();
            _client.Update();
            _accumulator -= GameClock.RealSecondsPerTick;
        }

        _view.Advance(delta);
        if (_view.Entity(_client.Replica.OwnEntityId) is EntityView own)
        {
            _camera.Follow(own.Position, delta);
            Cutaway.SetFocus(own.Position);
        }
        else
        {
            Cutaway.Disable();
        }

        _hud.Refresh();
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
