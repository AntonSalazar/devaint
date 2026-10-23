using System.Collections.Generic;

namespace DevAInt.Sim.Net;

/// <summary>Одна сторона подключения: пакеты доставляются целиком, надёжно и по порядку.</summary>
public interface IConnection
{
    /// <summary>Подключение живо.</summary>
    bool IsConnected { get; }

    /// <summary>Отправка пакета другой стороне.</summary>
    void Send(byte[] packet);

    /// <summary>Следующий полученный пакет, false если пакетов нет.</summary>
    bool TryReceive(out byte[] packet);

    /// <summary>Разрыв подключения с обеих сторон.</summary>
    void Disconnect();
}

/// <summary>Серверная сторона транспорта: новые подключения клиентов.</summary>
public interface IServerTransport
{
    /// <summary>Следующее новое подключение, false если новых нет.</summary>
    bool TryAccept(out IConnection connection);
}

/// <summary>Транспорт в памяти для одиночной игры и тестов: клиент и сервер в одном процессе.</summary>
public sealed class LoopbackTransport : IServerTransport
{
    /// <summary>Серверные стороны подключений, которые ещё не приняты.</summary>
    private readonly Queue<IConnection> _pending = new();

    /// <summary>Новое подключение клиента; серверная сторона ждёт TryAccept.</summary>
    public IConnection Connect()
    {
        LoopbackConnection client = new();
        LoopbackConnection server = new();
        client.Other = server;
        server.Other = client;
        _pending.Enqueue(server);
        return client;
    }

    /// <inheritdoc/>
    public bool TryAccept(out IConnection connection) => _pending.TryDequeue(out connection!);

    /// <summary>Сторона loopback-подключения с очередью входящих пакетов.</summary>
    private sealed class LoopbackConnection : IConnection
    {
        /// <summary>Входящие пакеты.</summary>
        private readonly Queue<byte[]> _incoming = new();

        /// <summary>Другая сторона.</summary>
        public LoopbackConnection? Other { get; set; }

        /// <inheritdoc/>
        public bool IsConnected { get; private set; } = true;

        /// <inheritdoc/>
        public void Send(byte[] packet)
        {
            if (IsConnected)
            {
                Other!._incoming.Enqueue(packet);
            }
        }

        /// <inheritdoc/>
        public bool TryReceive(out byte[] packet) => _incoming.TryDequeue(out packet!);

        /// <inheritdoc/>
        public void Disconnect()
        {
            IsConnected = false;
            Other!.IsConnected = false;
        }
    }
}
