using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты <see cref="LoopbackTransport"/>.</summary>
public class LoopbackTest
{
    /// <summary>Пакеты доходят в обе стороны по порядку.</summary>
    [Fact]
    public void DeliversInOrderBothWays()
    {
        LoopbackTransport transport = new();
        IConnection client = transport.Connect();
        Assert.True(transport.TryAccept(out IConnection server));
        Assert.False(transport.TryAccept(out _));

        client.Send([1]);
        client.Send([2]);
        server.Send([3]);

        Assert.True(server.TryReceive(out byte[] first));
        Assert.True(server.TryReceive(out byte[] second));
        Assert.False(server.TryReceive(out _));
        Assert.True(client.TryReceive(out byte[] reply));
        Assert.Equal([1], first);
        Assert.Equal([2], second);
        Assert.Equal([3], reply);
    }

    /// <summary>Разрыв с одной стороны виден обеим, после него пакеты не уходят.</summary>
    [Fact]
    public void DisconnectIsSeenByBoth()
    {
        LoopbackTransport transport = new();
        IConnection client = transport.Connect();
        transport.TryAccept(out IConnection server);

        client.Disconnect();
        client.Send([1]);

        Assert.False(client.IsConnected);
        Assert.False(server.IsConnected);
        Assert.False(server.TryReceive(out _));
    }
}
