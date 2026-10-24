using System;
using System.Collections.Generic;
using DevAInt.Sim.Net;
using Godot;

/// <summary>Серверная сторона ENet: принимает клиентов и раскладывает их пакеты по подключениям.</summary>
public sealed class EnetServerTransport : IServerTransport
{
    /// <summary>Пир ENet в режиме сервера.</summary>
    private readonly ENetMultiplayerPeer _peer = new();

    /// <summary>Подключения по номерам пиров.</summary>
    private readonly Dictionary<int, EnetPeerConnection> _connections = [];

    /// <summary>Новые подключения, которые ещё не приняты сервером.</summary>
    private readonly Queue<IConnection> _pending = new();

    /// <summary>Сервер на порту port; если порт занят, InvalidOperationException.</summary>
    public EnetServerTransport(int port, int maxClients)
    {
        Error error = _peer.CreateServer(port, maxClients);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"cannot listen on port {port}: {error}");
        }

        _peer.PeerConnected += OnPeerConnected;
        _peer.PeerDisconnected += OnPeerDisconnected;
    }

    /// <summary>Число подключённых клиентов.</summary>
    public int ConnectionCount => _connections.Count;

    /// <inheritdoc/>
    public bool TryAccept(out IConnection connection) => _pending.TryDequeue(out connection!);

    /// <summary>Обмен с сетью: отправка накопленного, новые и ушедшие клиенты, пришедшие пакеты; вызывать каждый кадр.</summary>
    public void Poll()
    {
        // Закрытый пир Godot опрашивать нельзя, это ошибка в логе
        if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected)
        {
            return;
        }

        _peer.Poll();
        while (_peer.GetAvailablePacketCount() > 0)
        {
            // Номер отправителя нужно взять до самого пакета
            int from = _peer.GetPacketPeer();
            byte[] packet = _peer.GetPacket();
            if (_connections.TryGetValue(from, out EnetPeerConnection? connection))
            {
                connection.Incoming.Enqueue(packet);
            }
        }
    }

    /// <summary>Остановка сервера: все клиенты отключаются.</summary>
    public void Close() => _peer.Close();

    /// <summary>Клиент подключился.</summary>
    private void OnPeerConnected(long id)
    {
        EnetPeerConnection connection = new(_peer, (int)id);
        _connections[(int)id] = connection;
        _pending.Enqueue(connection);
    }

    /// <summary>Клиент отключился: сервер увидит это по IsConnected.</summary>
    private void OnPeerDisconnected(long id)
    {
        if (_connections.Remove((int)id, out EnetPeerConnection? connection))
        {
            connection.MarkDisconnected();
        }
    }

    /// <summary>Подключение одного клиента на сервере.</summary>
    private sealed class EnetPeerConnection(ENetMultiplayerPeer peer, int id) : IConnection
    {
        /// <summary>Пришедшие пакеты клиента.</summary>
        public Queue<byte[]> Incoming { get; } = new();

        /// <inheritdoc/>
        public bool IsConnected { get; private set; } = true;

        /// <inheritdoc/>
        public void Send(byte[] packet)
        {
            if (!IsConnected)
            {
                return;
            }

            peer.SetTargetPeer(id);
            peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
            peer.PutPacket(packet);
        }

        /// <inheritdoc/>
        public bool TryReceive(out byte[] packet) => Incoming.TryDequeue(out packet!);

        /// <inheritdoc/>
        public void Disconnect()
        {
            if (IsConnected)
            {
                peer.DisconnectPeer(id);
                IsConnected = false;
            }
        }

        /// <summary>Клиент ушёл сам.</summary>
        public void MarkDisconnected() => IsConnected = false;
    }
}

/// <summary>Клиентская сторона ENet: одно подключение к серверу.</summary>
public sealed class EnetClientConnection : IConnection
{
    /// <summary>Пир ENet в режиме клиента.</summary>
    private readonly ENetMultiplayerPeer _peer = new();

    /// <summary>Пришедшие пакеты сервера.</summary>
    private readonly Queue<byte[]> _incoming = new();

    /// <summary>Пакеты, отправленные до подключения: уйдут сразу после него, первым - приветствие.</summary>
    private readonly Queue<byte[]> _outbox = new();

    /// <summary>Начало подключения к серверу address:port; ошибка создания пира - InvalidOperationException.</summary>
    public EnetClientConnection(string address, int port)
    {
        Error error = _peer.CreateClient(address, port);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"cannot connect to {address}:{port}: {error}");
        }
    }

    /// <summary>Подключение ещё устанавливается.</summary>
    public bool IsConnecting => _peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connecting;

    /// <inheritdoc/>
    public bool IsConnected => _peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    /// <summary>Обмен с сетью: отправка накопленного и приём пакетов; вызывать каждый кадр.</summary>
    public void Poll()
    {
        // Закрытый пир Godot опрашивать нельзя, это ошибка в логе
        if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected)
        {
            return;
        }

        _peer.Poll();
        while (IsConnected && _outbox.TryDequeue(out byte[]? packet))
        {
            Put(packet);
        }

        while (_peer.GetAvailablePacketCount() > 0)
        {
            _incoming.Enqueue(_peer.GetPacket());
        }
    }

    /// <inheritdoc/>
    public void Send(byte[] packet)
    {
        // Подключение обещает доставку по порядку, поэтому до подключения пакеты копятся, а после разрыва теряются
        if (IsConnecting || _outbox.Count > 0)
        {
            _outbox.Enqueue(packet);
        }
        else if (IsConnected)
        {
            Put(packet);
        }
    }

    /// <inheritdoc/>
    public bool TryReceive(out byte[] packet) => _incoming.TryDequeue(out packet!);

    /// <inheritdoc/>
    public void Disconnect() => _peer.Close();

    /// <summary>Пакет серверу надёжным каналом.</summary>
    private void Put(byte[] packet)
    {
        _peer.SetTargetPeer((int)MultiplayerPeer.TargetPeerServer);
        _peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        _peer.PutPacket(packet);
    }
}
