using DevAInt.Sim.Logging;
using Godot;

/// <summary>Лог sim в вывод Godot.</summary>
public sealed class GodotLog : ISimLog
{
    /// <inheritdoc/>
    public void Info(string message) => GD.Print(message);

    /// <inheritdoc/>
    public void Error(string message) => GD.PushError(message);
}
