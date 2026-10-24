using System;
using System.Collections.Generic;

namespace DevAInt.Sim.Net;

/// <summary>Обёртка над подключением, которая задерживает пакеты в обе стороны, для проверки игры с задержкой сети.</summary>
public sealed class LaggedConnection(IConnection inner, double oneWaySeconds, Func<double> now) : IConnection
{
    /// <summary>Исходящие пакеты и время, когда их можно отдать дальше.</summary>
    private readonly Queue<(double Due, byte[] Packet)> _outgoing = new();

    /// <summary>Входящие пакеты и время, когда их можно отдать клиенту.</summary>
    private readonly Queue<(double Due, byte[] Packet)> _incoming = new();

    /// <inheritdoc/>
    public bool IsConnected => inner.IsConnected;

    /// <inheritdoc/>
    public void Send(byte[] packet)
    {
        _outgoing.Enqueue((now() + oneWaySeconds, packet));
        Flush();
    }

    /// <inheritdoc/>
    public bool TryReceive(out byte[] packet)
    {
        Flush();

        // Пакет считается пришедшим в момент, когда его забрали из внутреннего подключения
        while (inner.TryReceive(out byte[] arrived))
        {
            _incoming.Enqueue((now() + oneWaySeconds, arrived));
        }

        if (_incoming.TryPeek(out (double Due, byte[] Packet) next) && next.Due <= now())
        {
            packet = _incoming.Dequeue().Packet;
            return true;
        }

        packet = [];
        return false;
    }

    /// <inheritdoc/>
    public void Disconnect() => inner.Disconnect();

    /// <summary>Отправка исходящих пакетов, чьё время пришло.</summary>
    private void Flush()
    {
        double time = now();
        while (_outgoing.TryPeek(out (double Due, byte[] Packet) next) && next.Due <= time)
        {
            inner.Send(_outgoing.Dequeue().Packet);
        }
    }
}
