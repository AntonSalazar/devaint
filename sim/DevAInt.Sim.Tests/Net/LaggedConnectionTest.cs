using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты <see cref="LaggedConnection"/>.</summary>
public class LaggedConnectionTest
{
    /// <summary>Текущее время для обёртки.</summary>
    private double _time;

    /// <summary>Исходящий пакет доходит до сервера только через задержку.</summary>
    [Fact]
    public void DelaysOutgoing()
    {
        LoopbackTransport transport = new();
        LaggedConnection client = new(transport.Connect(), 0.05, () => _time);
        transport.TryAccept(out IConnection server);

        client.Send([1]);
        _time = 0.04;
        client.TryReceive(out _);
        bool early = server.TryReceive(out _);
        _time = 0.05;
        client.TryReceive(out _);

        Assert.False(early);
        Assert.True(server.TryReceive(out byte[] packet));
        Assert.Equal([1], packet);
    }

    /// <summary>Входящие пакеты отдаются через задержку и по порядку.</summary>
    [Fact]
    public void DelaysIncomingInOrder()
    {
        LoopbackTransport transport = new();
        LaggedConnection client = new(transport.Connect(), 0.05, () => _time);
        transport.TryAccept(out IConnection server);
        server.Send([1]);
        server.Send([2]);

        bool early = client.TryReceive(out _);
        _time = 0.05;

        Assert.False(early);
        Assert.True(client.TryReceive(out byte[] first));
        Assert.True(client.TryReceive(out byte[] second));
        Assert.False(client.TryReceive(out _));
        Assert.Equal([1], first);
        Assert.Equal([2], second);
    }

    /// <summary>Разрыв проходит сквозь обёртку сразу.</summary>
    [Fact]
    public void DisconnectPassesThrough()
    {
        LoopbackTransport transport = new();
        LaggedConnection client = new(transport.Connect(), 0.05, () => _time);
        transport.TryAccept(out IConnection server);

        client.Disconnect();

        Assert.False(client.IsConnected);
        Assert.False(server.IsConnected);
    }
}
