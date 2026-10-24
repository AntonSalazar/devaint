using System.Collections.Generic;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Net;

/// <summary>Клиент: принимает пакеты сервера в реплику, отправляет ввод и предсказывает своё движение.</summary>
public sealed class GameClient
{
    /// <summary>Подключение к серверу.</summary>
    private readonly IConnection _connection;

    /// <summary>Предсказание своего движения.</summary>
    private readonly MovePrediction _prediction = new();

    /// <summary>Номер последнего отправленного ввода.</summary>
    private uint _inputSequence;

    /// <summary>Клиент игрока playerId на подключении; mods - свои моды для сверки, приветствие уходит сразу.</summary>
    public GameClient(IConnection connection, StatTable stats, ItemTable items, IReadOnlyList<ModInfo> mods, string playerId)
    {
        _connection = connection;
        Replica = new Replica(stats, items, mods);
        connection.Send(Protocol.Write(new HelloMessage(playerId)));
    }

    /// <summary>Копия мира.</summary>
    public Replica Replica { get; }

    /// <summary>Подключение к серверу живо.</summary>
    public bool IsConnected => _connection.IsConnected;

    /// <summary>Предсказанная позиция своей сущности, null пока она неизвестна.</summary>
    public WorldPos? OwnPosition => _prediction.Position;

    /// <summary>Применение всех пришедших пакетов по порядку и сверка предсказания с сервером.</summary>
    public void Update()
    {
        bool received = false;
        while (_connection.TryReceive(out byte[] packet))
        {
            Replica.Apply(Protocol.ReadServerFrame(packet));
            received = true;
        }

        if (received && Replica.Entity(Replica.OwnEntityId) is ReplicaEntity own)
        {
            WorldPos server = new(own.X, own.Y, own.Z);
            _prediction.Reconcile(Replica.LastInput, server, Replica.World!, Replica.SecondsPerTick);
        }
    }

    /// <summary>Отправка ввода движения с новым номером; своя сущность сразу двигается по предсказанию.</summary>
    public void SendInput(MoveInput input)
    {
        _inputSequence++;
        _connection.Send(Protocol.Write(new InputMessage(_inputSequence, (float)input.X, (float)input.Y)));

        // Предсказание идёт по тому же числу во float, которое получит сервер
        MoveInput sent = new((float)input.X, (float)input.Y);
        if (Replica.World is World world)
        {
            _prediction.Add(_inputSequence, sent, world, Replica.SecondsPerTick);
        }
    }

    /// <summary>Просьба начать действие предметом; номер действия из ItemTable.</summary>
    public void SendStartAction(long itemId, int action) => _connection.Send(Protocol.Write(new StartActionMessage(itemId, action)));

    /// <summary>Просьба прервать своё действие.</summary>
    public void SendCancelAction() => _connection.Send(Protocol.Write(new CancelActionMessage()));
}
