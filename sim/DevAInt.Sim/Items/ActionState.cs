using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Items;

/// <summary>Итог попытки начать действие.</summary>
public enum ActionStart
{
    /// <summary>Действие началось.</summary>
    Started,

    /// <summary>Уже идёт другое действие.</summary>
    Busy,

    /// <summary>Такого предмета нет в инвентаре.</summary>
    NoItem,

    /// <summary>У предмета нет такого применения.</summary>
    NoUse,
}

/// <summary>Чем закончилось последнее действие.</summary>
public enum ActionEnd
{
    /// <summary>Действий ещё не было.</summary>
    None,

    /// <summary>Выполнено до конца.</summary>
    Completed,

    /// <summary>Прервано движением или отменой.</summary>
    Interrupted,
}

/// <summary>Идущее действие: что делается, с каким предметом, с какого тика и сколько тиков.</summary>
public sealed record ActionState(ActionDef Action, ItemUseDef Use, long ItemId, long StartTick, int DurationTicks)
{
    /// <summary>Тик, на котором действие закончится.</summary>
    public long EndTick => StartTick + DurationTicks;
}
