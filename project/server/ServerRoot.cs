using System;
using DevAInt.Sim;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;
using DevAInt.Sim.Stats;
using Godot;

/// <summary>Выделенный сервер: моды, sim, ENet и цикл тиков 10 Гц, без игровых узлов.</summary>
public partial class ServerRoot : Node
{
    /// <summary>Наибольшее число игроков на сервере.</summary>
    public const int MaxClients = 32;

    /// <summary>Кадров в секунду сервера: ENet опрашивается каждый кадр, без предела headless-процесс занял бы ядро целиком.</summary>
    private const int MaxFps = 60;

    /// <summary>Лог.</summary>
    private readonly GodotLog _log = new();

    /// <summary>Счётчик времени до тиков.</summary>
    private readonly TickTimer _timer = new();

    /// <summary>Сетевой транспорт.</summary>
    private EnetServerTransport? _transport;

    /// <summary>Сервер игры.</summary>
    private GameServer? _server;

    /// <summary>Число клиентов на прошлом тике, для строки в логе при входе и выходе.</summary>
    private int _clientCount;

    /// <summary>Загрузка модов, запуск sim и ENet; при ошибке процесс завершается с кодом 1.</summary>
    public override void _Ready()
    {
        Engine.MaxFps = MaxFps;
        string[] args = OS.GetCmdlineUserArgs();
        if (GameMods.Load(_log) is not ModLoadResult loaded)
        {
            GetTree().Quit(1);
            return;
        }

        Simulation sim = new(loaded, _log, GameRoot.MapId, new GameClock(GameArgs.TimeScale(args)));
        foreach (ModError error in sim.ScriptErrors)
        {
            _log.Error(error.ToString());
        }

        int port = GameArgs.Port(args);
        try
        {
            _transport = new EnetServerTransport(port, MaxClients);
        }
        catch (InvalidOperationException error)
        {
            _log.Error(error.Message);
            GetTree().Quit(1);
            return;
        }

        _server = new GameServer(sim, _transport, _log);
        _log.Info($"server is listening on port {port}, time scale {sim.Clock.TimeScale}");
    }

    /// <summary>Кадр: обмен с сетью и тики по накопленному времени.</summary>
    public override void _Process(double delta)
    {
        if (_transport is null || _server is null)
        {
            return;
        }

        _transport.Poll();
        for (int ticks = _timer.Advance(delta); ticks > 0; ticks--)
        {
            _server.Tick();
        }

        if (_server.ClientCount != _clientCount)
        {
            _clientCount = _server.ClientCount;
            _log.Info($"clients: {_clientCount}");
        }
    }

    /// <summary>Остановка сети при выходе.</summary>
    public override void _ExitTree() => _transport?.Close();
}
