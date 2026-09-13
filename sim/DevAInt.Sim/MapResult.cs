namespace DevAInt.Sim;


/// <summary>
/// Результат генерации карты.
/// </summary>
/// <param name="World">Инфа о мире.</param>
/// <param name="Start">Стартовый гекс daemon'а (Home в центре жилого кластера).</param>
/// <param name="Target">Цель забега — сервер в центре датацентра.</param>
/// <param name="SeedUsed">Сид, с которого карта реально сгенерировалась (после повторов).</param>
public sealed record MapResult(World World, Hex Start, Hex Target, int SeedUsed);
