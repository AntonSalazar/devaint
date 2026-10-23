using System;

namespace DevAInt.Sim.Grid;

/// <summary>Точка мира в метрах: клетка x, y занимает от x до x + 1 и от y до y + 1; z - этаж.</summary>
public readonly record struct WorldPos(double X, double Y, int Z)
{
    /// <summary>Клетка, в которой лежит точка.</summary>
    public CellPos Cell => new((int)Math.Floor(X), (int)Math.Floor(Y), Z);

    /// <summary>Центр клетки.</summary>
    public static WorldPos CenterOf(CellPos cell) => new(cell.X + 0.5, cell.Y + 0.5, cell.Z);
}
