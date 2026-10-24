using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;

namespace DevAInt.Sim;

/// <summary>Сущность sim: id, вид, статы, позиция, ввод движения, инвентарь, действие и поведение.</summary>
public sealed class Entity(long id, StatSet stats, WorldPos position, CreatureDef? creature = null)
{
    /// <summary>Уникальный id.</summary>
    public long Id { get; } = id;

    /// <summary>Вид существа, null - сущность без вида со всеми статами.</summary>
    public CreatureDef? Creature { get; } = creature;

    /// <summary>Тело для движения: из вида или по умолчанию.</summary>
    public MoveParams Body => Creature?.Body ?? MoveParams.Default;

    /// <summary>Куда смотрит: последнее направление движения, единичный вектор.</summary>
    public (double X, double Y) Facing { get; internal set; } = (0, 1);

    /// <summary>Тик, с которого атака снова готова.</summary>
    public long AttackReadyTick { get; internal set; }

    /// <summary>Игрок, которому принадлежит сущность, null у остальных сущностей.</summary>
    public string? PlayerId { get; internal set; }

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

    /// <summary>Игрок просит атаковать на этом тике.</summary>
    internal bool AttackRequested { get; set; }

    /// <summary>Память поведения, null у существ без поведения.</summary>
    internal BrainState? Brain { get; set; }
}
