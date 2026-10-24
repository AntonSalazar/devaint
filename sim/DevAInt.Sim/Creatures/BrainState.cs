using System.Collections.Generic;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Память поведения существа: цель, где её видели в последний раз и путь к ней.</summary>
internal sealed class BrainState
{
    /// <summary>Клетки пути по порядку.</summary>
    public List<CellPos> Path { get; } = [];

    /// <summary>Номер следующей клетки пути.</summary>
    public int PathIndex { get; set; }

    /// <summary>Клетка, к которой построен путь.</summary>
    public CellPos? PathGoal { get; set; }

    /// <summary>Тик, после которого путь строится заново.</summary>
    public long RepathTick { get; set; }

    /// <summary>Тик следующего поиска цели.</summary>
    public long ScanTick { get; set; }

    /// <summary>Цель или null.</summary>
    public Entity? Target { get; set; }

    /// <summary>Где цель видели в последний раз, null - идти некуда.</summary>
    public WorldPos? LastSeen { get; set; }
}
