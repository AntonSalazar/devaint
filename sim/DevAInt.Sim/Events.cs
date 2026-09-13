namespace DevAInt.Sim;


/// <summary>
/// Событие симуляции - "новость" о том, что произошло при применении действия.
/// Это не правило и не состояние: лог, HUD и анимации читают события,
/// симуляция про них забывает сразу после возврата из <see cref="Sim.Apply"/>.
/// </summary>
/// <param name="Turn">На каком ходу произошло.</param>
public abstract record SimEvent(int Turn);


/// <summary>
/// Узел захвачен.
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="Node">Какой узел.</param>
public sealed record NodeCaptured(int Turn, Hex Node) : SimEvent(Turn);


/// <summary>
/// Попытка захвата не удалась: очко потрачено, заряд цел, узел раскрыт.
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="Node">Какой узел.</param>
/// <param name="Reason">Почему.</param>
public sealed record CaptureFailed(int Turn, Hex Node, string Reason) : SimEvent(Turn);


/// <summary>
/// Эксплойт скомпилирован в слот.
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="DefId">Id эксплойта.</param>
public sealed record ExploitCompiled(int Turn, string DefId) : SimEvent(Turn);


/// <summary>
/// Эксплойт выгорел: уязвимость закрыли, Power упал.
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="DefId">Id эксплойта.</param>
/// <param name="NewPower">Сила после выгорания.</param>
public sealed record ExploitBurned(int Turn, string DefId, int NewPower) : SimEvent(Turn);


/// <summary>
/// Узел вычищен аудитом.
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="Node">Какой узел.</param>
public sealed record NodeAudited(int Turn, Hex Node) : SimEvent(Turn);


/// <summary>
/// Сработало мировое событие (патч-волна, отключение линка, миграция).
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="Id">Id события из <c>events.json</c>.</param>
/// <param name="Text">Готовый текст для лога.</param>
public sealed record WorldEventFired(int Turn, string Id, string Text) : SimEvent(Turn);


/// <summary>
/// Начался новый ход: upkeep прошёл.
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="Income">Сколько Compute пришло.</param>
public sealed record TurnStarted(int Turn, int Income) : SimEvent(Turn);


/// <summary>
/// Забег окончен.
/// </summary>
/// <param name="Turn">Ход.</param>
/// <param name="Outcome">Чем.</param>
/// <param name="Score">Счёт.</param>
public sealed record RunEnded(int Turn, Outcome Outcome, int Score) : SimEvent(Turn);
