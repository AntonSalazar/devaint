using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim;

/// <summary>Сущность sim: id, статы, позиция и текущий ввод движения.</summary>
public sealed class Entity(long id, StatSet stats, WorldPos position)
{
    /// <summary>Уникальный id.</summary>
    public long Id { get; } = id;

    /// <summary>Статы и мудлы.</summary>
    public StatSet Stats { get; } = stats;

    /// <summary>Позиция, меняется движением в тике.</summary>
    public WorldPos Position { get; internal set; } = position;

    /// <summary>Ввод движения, действует каждый тик, пока его не сменят.</summary>
    public MoveInput Input { get; set; } = MoveInput.None;
}
