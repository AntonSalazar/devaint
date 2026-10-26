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

    /// <summary>У предмета нет такого применения или нет такого рецепта.</summary>
    NoUse,

    /// <summary>Результат не влезет по весу.</summary>
    Full,

    /// <summary>Постройку или разбор нельзя сделать на этом месте.</summary>
    BadPlace,
}

/// <summary>Итог попытки переложить предмет.</summary>
public enum ItemMove
{
    /// <summary>Предмет переложен.</summary>
    Moved,

    /// <summary>Такого предмета нет там, откуда его берут.</summary>
    NoItem,

    /// <summary>Объекта нет или в него ничего не положить.</summary>
    NoContainer,

    /// <summary>Контейнер дальше вытянутой руки или за стеной.</summary>
    OutOfReach,

    /// <summary>Не хватает вместимости.</summary>
    Full,

    /// <summary>Предмет сейчас в руках идущего действия.</summary>
    InUse,

    /// <summary>Откуда и куда - одно и то же место.</summary>
    SamePlace,
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

/// <summary>Идущее действие: что делается, с какого тика и сколько тиков; применение предмета Use с предметом ItemId или рецепт Recipe.</summary>
public sealed record ActionState(ActionDef Action, ItemUseDef? Use, long ItemId, long StartTick, int DurationTicks)
{
    /// <summary>Рецепт крафта, null - действие не крафт.</summary>
    public RecipeDef? Recipe { get; init; }

    /// <summary>Цель постройки или разбора, null - действие не постройка.</summary>
    public BuildTarget? Build { get; init; }

    /// <summary>Тик, на котором действие закончится.</summary>
    public long EndTick => StartTick + DurationTicks;
}
