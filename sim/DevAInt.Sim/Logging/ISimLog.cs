namespace DevAInt.Sim.Logging;

/// <summary>Лог sim: сервер направляет его в вывод Godot, тесты собирают в список.</summary>
public interface ISimLog
{
    /// <summary>Обычное сообщение.</summary>
    void Info(string message);

    /// <summary>Ошибка, после которой sim продолжает работу.</summary>
    void Error(string message);
}
