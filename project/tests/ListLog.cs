using System.Collections.Generic;
using DevAInt.Sim.Logging;

/// <summary>Лог для тестов, который запоминает сообщения.</summary>
public sealed class ListLog : ISimLog
{
    /// <summary>Сообщения по порядку.</summary>
    public List<string> Infos { get; } = [];

    /// <summary>Ошибки по порядку.</summary>
    public List<string> Errors { get; } = [];

    /// <inheritdoc/>
    public void Info(string message) => Infos.Add(message);

    /// <inheritdoc/>
    public void Error(string message) => Errors.Add(message);
}
