using System.Collections.Generic;
using DevAInt.Sim.Logging;

namespace DevAInt.Sim.Tests.Lua;

/// <summary>Лог в списки для проверок в тестах.</summary>
public sealed class ListLog : ISimLog
{
    /// <summary>Обычные сообщения.</summary>
    public List<string> Infos { get; } = [];

    /// <summary>Ошибки.</summary>
    public List<string> Errors { get; } = [];

    /// <inheritdoc/>
    public void Info(string message) => Infos.Add(message);

    /// <inheritdoc/>
    public void Error(string message) => Errors.Add(message);
}
