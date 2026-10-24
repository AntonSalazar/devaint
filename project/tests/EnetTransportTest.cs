using System;
using DevAInt.Sim.Net;
using Godot;

/// <summary>Тесты <see cref="EnetServerTransport"/> и <see cref="EnetClientConnection"/> на настоящем ENet через localhost.</summary>
public class EnetTransportTest : TestCase
{
    /// <summary>Порт тестового сервера.</summary>
    public const int Port = 47_311;

    /// <summary>Наибольшее время ожидания сети в тесте.</summary>
    private const int TimeoutMs = 3000;

    /// <summary>Сервер.</summary>
    private EnetServerTransport _server = null!;

    /// <summary>Клиент.</summary>
    private EnetClientConnection _client = null!;

    /// <summary>Обмен с сетью обеих сторон, пока условие не выполнится или не выйдет время; true - выполнилось.</summary>
    public static bool PollUntil(Func<bool> condition, EnetServerTransport server, params EnetClientConnection[] clients)
    {
        ulong deadline = Time.GetTicksMsec() + TimeoutMs;
        while (Time.GetTicksMsec() < deadline)
        {
            server.Poll();
            foreach (EnetClientConnection client in clients)
            {
                client.Poll();
            }

            if (condition())
            {
                return true;
            }

            OS.DelayMsec(1);
        }

        return false;
    }

    /// <summary>Новые сервер и клиент перед каждым тестом.</summary>
    public override void BeforeEach()
    {
        _server = new EnetServerTransport(Port, 4);
        _client = new EnetClientConnection("127.0.0.1", Port);
    }

    /// <summary>Закрытие обеих сторон после теста.</summary>
    public override void AfterEach()
    {
        _client.Disconnect();
        _server.Close();
    }

    /// <summary>Клиент подключается, пакеты доходят в обе стороны целиком и по порядку.</summary>
    public void TestPacketsBothWays()
    {
        IConnection? accepted = null;
        bool connected = PollUntil(() => _client.IsConnected && _server.TryAccept(out accepted), _server, _client);
        Check(connected, "client connected");
        if (accepted is null)
        {
            return;
        }

        byte[] big = new byte[5000];
        big[4999] = 7;
        _client.Send([1, 2]);
        _client.Send(big);
        accepted.Send([3]);
        byte[]? first = null;
        byte[]? second = null;
        byte[]? reply = null;
        PollUntil(() => (first ??= Receive(accepted)) is not null && (second ??= Receive(accepted)) is not null && (reply ??= Receive(_client)) is not null, _server, _client);

        CheckEqual("1,2", first is null ? "none" : string.Join(",", first), "first packet");
        CheckEqual(5000, second?.Length ?? 0, "big packet length");
        CheckEqual((byte)7, second?[4999] ?? 0, "big packet tail");
        CheckEqual("3", reply is null ? "none" : string.Join(",", reply), "reply");
    }

    /// <summary>Пакеты, отправленные до подключения, доходят после него по порядку.</summary>
    public void TestPacketsBeforeConnectArrive()
    {
        _client.Send([1]);
        _client.Send([2]);
        Check(_client.IsConnecting, "client is still connecting");

        IConnection? accepted = null;
        byte[]? first = null;
        byte[]? second = null;
        PollUntil(() => (accepted ??= Accept()) is not null && (first ??= Receive(accepted!)) is not null && (second ??= Receive(accepted!)) is not null, _server, _client);

        CheckEqual("1", first is null ? "none" : string.Join(",", first), "first packet");
        CheckEqual("2", second is null ? "none" : string.Join(",", second), "second packet");
    }

    /// <summary>Отключение клиента сервер видит по IsConnected.</summary>
    public void TestClientDisconnectIsSeen()
    {
        IConnection? accepted = null;
        PollUntil(() => _server.TryAccept(out accepted), _server, _client);
        Check(accepted is not null, "accepted");

        _client.Disconnect();
        bool seen = PollUntil(() => accepted is { IsConnected: false }, _server);

        Check(seen, "server sees disconnect");
        CheckEqual(0, _server.ConnectionCount, "connections");
    }

    /// <summary>Отключение клиента сервером, например за неверный пакет, клиент видит.</summary>
    public void TestServerKickIsSeen()
    {
        IConnection? accepted = null;
        PollUntil(() => _client.IsConnected && _server.TryAccept(out accepted), _server, _client);

        accepted?.Disconnect();
        bool seen = PollUntil(() => !_client.IsConnected && !_client.IsConnecting, _server, _client);

        Check(accepted is { IsConnected: false }, "server side is disconnected");
        Check(seen, "client sees kick");
    }

    /// <summary>Остановку сервера клиент видит: он больше не подключён и не подключается.</summary>
    public void TestServerCloseIsSeen()
    {
        // Сервер узнаёт о клиенте чуть позже, чем клиент о сервере
        PollUntil(() => _client.IsConnected && _server.ConnectionCount == 1, _server, _client);

        _server.Close();
        bool seen = PollUntil(() => !_client.IsConnected && !_client.IsConnecting, _server, _client);

        Check(seen, "client sees server close");
    }

    /// <summary>Следующий пакет подключения или null.</summary>
    private static byte[]? Receive(IConnection connection) => connection.TryReceive(out byte[] packet) ? packet : null;

    /// <summary>Новое подключение на сервере или null.</summary>
    private IConnection? Accept() => _server.TryAccept(out IConnection connection) ? connection : null;
}
