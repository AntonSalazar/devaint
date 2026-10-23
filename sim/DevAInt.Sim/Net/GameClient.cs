using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim.Net;

/// <summary>Клиент: принимает пакеты сервера в реплику и отправляет ввод.</summary>
public sealed class GameClient(IConnection connection, StatTable stats)
{
    /// <summary>Номер последнего отправленного ввода.</summary>
    private uint _inputSequence;

    /// <summary>Копия мира.</summary>
    public Replica Replica { get; } = new(stats);

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
}
