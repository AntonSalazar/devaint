using DevAInt.Sim;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using Godot;

/// <summary>Итог этапа 10: сервер и два клиента на настоящем ENet через localhost.</summary>
public class NetworkGameTest : TestCase
{
    /// <summary>Порт тестового сервера, отдельный от теста транспорта.</summary>
    private const int Port = EnetTransportTest.Port + 1;

    /// <summary>Допуск координат.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>Таблицы и sim сервера.</summary>
    private Simulation _sim = null!;

    /// <summary>Транспорт сервера.</summary>
    private EnetServerTransport _transport = null!;

    /// <summary>Сервер.</summary>
    private GameServer _server = null!;

    /// <summary>Сколько клиентов создано, для id игроков.</summary>
    private int _players;

    /// <summary>Сеть первого клиента.</summary>
    private EnetClientConnection _netA = null!;

    /// <summary>Сеть второго клиента.</summary>
    private EnetClientConnection _netB = null!;

    /// <summary>Сервер на ENet и два подключения к нему.</summary>
    public override void BeforeEach()
    {
        // Только core: зомби мешали бы проверять, что клиенты видят именно друг друга
        ModLoadResult loaded = ModLoader.Load(ModsPath.Find(), ["core"]);
        _sim = new Simulation(loaded, new GodotLog(), GameRoot.MapId);
        _transport = new EnetServerTransport(Port, 4);
        _server = new GameServer(_sim, _transport, new GodotLog());
        _netA = new EnetClientConnection("127.0.0.1", Port);
        _netB = new EnetClientConnection("127.0.0.1", Port);
    }

    /// <summary>Отключение клиентов и остановка сервера.</summary>
    public override void AfterEach()
    {
        _netA.Disconnect();
        _netB.Disconnect();
        _transport.Close();
    }

    /// <summary>Клиенты видят друг друга, и движение одного видно другому.</summary>
    public void TestClientsSeeEachOther()
    {
        GameClient a = Client(_netA);
        GameClient b = Client(_netB);

        bool joined = Run(() => a.Replica.Entities.Count == 2 && b.Replica.Entities.Count == 2, a, b);
        Check(joined, "both clients see two entities");
        long aId = a.Replica.OwnEntityId;
        Check(aId != 0 && aId != b.Replica.OwnEntityId, "different own entities");

        // Сервер повторяет последний ввод, поэтому одного ввода хватает, чтобы идти дальше
        a.SendInput(new MoveInput(1, 0));
        bool seen = Run(() => b.Replica.Entity(aId)?.X > 7.5f, a, b);

        Check(seen, "client b sees client a walking east");
    }

    /// <summary>При задержке 100 мс туда-обратно своя сущность трогается сразу, а сервер догоняет.</summary>
    public void TestOwnMovementIsResponsiveWithLag()
    {
        LaggedConnection lagged = new(_netA, 0.05, () => Time.GetTicksUsec() / 1e6);
        GameClient a = Client(lagged);
        GameClient b = Client(_netB);
        Run(() => a.OwnPosition is not null && b.Replica.Entities.Count == 2, a, b);
        ReplicaEntity own = a.Replica.Entity(a.Replica.OwnEntityId)!;
        double start = a.OwnPosition?.X ?? 0;

        a.SendInput(new MoveInput(1, 0));
        double predicted = a.OwnPosition?.X ?? 0;
        float server = own.X;
        bool confirmed = Run(() => own.X >= predicted - Tolerance, a, b);

        CheckNear(start + 0.15, predicted, Tolerance, "own position moved at once");
        CheckNear(start, server, Tolerance, "server position had not moved yet");
        Check(confirmed, "server reached the predicted position");
    }

    /// <summary>Клиент на подключении.</summary>
    private GameClient Client(IConnection connection) => new(connection, ClientTables.From(_sim), $"player{++_players}");

    /// <summary>Тики сервера и приём клиентов, пока условие не выполнится; true - выполнилось.</summary>
    private bool Run(System.Func<bool> condition, params GameClient[] clients)
    {
        return EnetTransportTest.PollUntil(
            () =>
            {
                _server.Tick();
                foreach (GameClient client in clients)
                {
                    client.Update();
                }

                return condition();
            },
            _transport,
            _netA,
            _netB);
    }
}
