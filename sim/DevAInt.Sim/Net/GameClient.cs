using DevAInt.Sim.Items;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Net;

/// <summary>Клиент: принимает пакеты сервера в реплику и отправляет ввод.</summary>
public sealed class GameClient(IConnection connection, StatTable stats, ItemTable items)
{
    /// <summary>Номер последнего отправленного ввода.</summary>
    private uint _inputSequence;

    /// <summary>Копия мира.</summary>
    public Replica Replica { get; } = new(stats, items);

    /// <summary>Подключение к серверу живо.</summary>
    public bool IsConnected => connection.IsConnected;

    /// <summary>Применение всех пришедших пакетов по порядку.</summary>
    public void Update()
    {
        while (connection.TryReceive(out byte[] packet))
        {
            Replica.Apply(Protocol.ReadServerFrame(packet));
        }
    }

    /// <summary>Отправка ввода движения с новым номером.</summary>
    public void SendInput(MoveInput input)
    {
        _inputSequence++;
        connection.Send(Protocol.Write(new InputMessage(_inputSequence, (float)input.X, (float)input.Y)));
    }

    /// <summary>Просьба начать действие предметом; номер действия из ItemTable.</summary>
    public void SendStartAction(long itemId, int action) => connection.Send(Protocol.Write(new StartActionMessage(itemId, action)));

    /// <summary>Просьба прервать своё действие.</summary>
    public void SendCancelAction() => connection.Send(Protocol.Write(new CancelActionMessage()));
}
