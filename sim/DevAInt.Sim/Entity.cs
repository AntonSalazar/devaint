using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim;

/// <summary>Сущность sim: id, статы, позиция, ввод движения, инвентарь и текущее действие.</summary>
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

    /// <summary>Предметы.</summary>
    public Inventory Inventory { get; } = new();

    /// <summary>Идущее действие или null.</summary>
    public ActionState? Action { get; internal set; }

    /// <summary>Чем закончилось последнее действие.</summary>
    public ActionEnd LastActionEnd { get; internal set; }
}
