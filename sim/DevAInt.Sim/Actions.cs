namespace DevAInt.Sim;


/// <summary>
/// Действие игрока. Базовый тип: сам по себе ничего не значит, нужен, чтобы
/// <see cref="Sim.Check"/> и <see cref="Sim.Apply"/> принимали "любое действие"
/// и внутри разбирали через <c>switch</c>, какое именно пришло.
/// Все действия - record: неизменяемые, сравниваются по полям, легко пишутся в JSON
/// (реплей = сид + список действий).
/// </summary>
public abstract record Action;


/// <summary>
/// Перейти на соседний свой узел.
/// </summary>
/// <param name="To">Куда.</param>
public sealed record Move(Hex To) : Action;


/// <summary>
/// Применить эксплойт из слота на соседний узел.
/// </summary>
/// <param name="SlotIx">Номер слота у daemon'а.</param>
/// <param name="Target">Цель.</param>
public sealed record Exploit(int SlotIx, Hex Target) : Action;


/// <summary>
/// Снять туман вокруг daemon'а.
/// </summary>
public sealed record Scan : Action;


/// <summary>
/// Обнулить шум на узле под daemon'ом.
/// </summary>
public sealed record Lurk : Action;


/// <summary>
/// Скомпилировать эксплойт на своём сервере под daemon'ом.
/// </summary>
/// <param name="ExploitId">Id эксплойта.</param>
/// <param name="SlotIx">В какой слот положить: пустой (равен числу слотов) или занятый (замена).</param>
public sealed record Compile(string ExploitId, int SlotIx) : Action;


/// <summary>
/// Закончить ход: мировая фаза, затем начало следующего хода.
/// </summary>
public sealed record EndTurn : Action;


/// <summary>
/// Ответ на вопрос "можно ли": да, или нет и почему.
/// Причина - для UI ("не хватает очков действия") и для отладки.
/// </summary>
/// <param name="Ok">Можно ли.</param>
/// <param name="Reason">Почему нельзя; пустая строка, если можно.</param>
public sealed record Verdict(bool Ok, string Reason)
{
    /// <summary>
    /// Готовый ответ "можно".
    /// </summary>
    public static readonly Verdict Yes = new(true, string.Empty);


    /// <summary>
    /// Статичный метод ответа "нельзя" с причиной.
    /// </summary>
    /// <param name="reason">Причина.</param>
    /// <returns>Вернет вердикт.</returns>
    public static Verdict No(string reason) => new(false, reason);
}
