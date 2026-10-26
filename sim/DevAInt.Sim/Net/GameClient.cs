using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;

namespace DevAInt.Sim.Net;

/// <summary>Клиент: принимает пакеты сервера в реплику, отправляет ввод и предсказывает своё движение.</summary>
public sealed class GameClient
{
    /// <summary>Подключение к серверу.</summary>
    private readonly IConnection _connection;

    /// <summary>Предсказание своего движения.</summary>
    private readonly MovePrediction _prediction = new();

    /// <summary>Таблицы номеров, для тела своего существа.</summary>
    private readonly ClientTables _tables;

    /// <summary>Сущность, движение которой предсказывается.</summary>
    private long _predictedEntity;

    /// <summary>Номер последнего отправленного ввода.</summary>
    private uint _inputSequence;

    /// <summary>Клиент игрока playerId на подключении; приветствие уходит сразу.</summary>
    public GameClient(IConnection connection, ClientTables tables, string playerId)
    {
        _connection = connection;
        _tables = tables;
        Replica = new Replica(tables);
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

        // После смерти своя сущность новая: вводы прежней к ней не относятся
        if (Replica.OwnEntityId != _predictedEntity)
        {
            _prediction.Reset();
            _predictedEntity = Replica.OwnEntityId;
        }

        if (received && Replica.Entity(Replica.OwnEntityId) is ReplicaEntity own)
        {
            WorldPos server = new(own.X, own.Y, own.Z);
            _prediction.Reconcile(Replica.LastInput, server, Replica.World!, Replica.SecondsPerTick, OwnBody());
        }
    }

    /// <summary>Отправка ввода движения с новым номером, точкой под курсором и просьбой смотреть на неё; своя сущность сразу двигается по предсказанию.</summary>
    public void SendInput(MoveInput input, WorldPos? aim = null, bool faceAim = false)
    {
        _inputSequence++;
        WorldPos point = aim ?? default;
        _connection.Send(Protocol.Write(new InputMessage(_inputSequence, (float)input.X, (float)input.Y, (float)point.X, (float)point.Y, faceAim)));

        // Предсказание идёт по тому же числу во float, которое получит сервер
        MoveInput sent = new((float)input.X, (float)input.Y);
        if (Replica.World is World world)
        {
            _prediction.Add(_inputSequence, sent, world, Replica.SecondsPerTick, OwnBody());
        }
    }

    /// <summary>Просьба начать действие предметом; номер действия из ItemTable.</summary>
    public void SendStartAction(long itemId, int action) => _connection.Send(Protocol.Write(new StartActionMessage(itemId, action)));

    /// <summary>Действие управления нажато или отпущено: номер из ControlTable.</summary>
    public void SendControl(int control, bool pressed) => _connection.Send(Protocol.Write(new ControlMessage(control, pressed)));

    /// <summary>Просьба прервать своё действие.</summary>
    public void SendCancelAction() => _connection.Send(Protocol.Write(new CancelActionMessage()));

    /// <summary>Тело своего существа: радиус и скорость из вида, как на сервере.</summary>
    private MoveParams OwnBody()
    {
        int creature = Replica.Entity(Replica.OwnEntityId)?.Creature ?? -1;
        return creature >= 0 ? _tables.Creatures.Creature(creature).Body : MoveParams.Default;
    }
}
