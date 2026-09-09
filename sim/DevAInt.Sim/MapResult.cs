using System.Collections.Generic;

namespace DevAInt.Sim;


/// <summary>
/// Результат генерации карты.
/// </summary>
/// <param name="World">Инфа о мире.</param>
/// <param name="Starts">Стартовые гексы по фракциям: <c>Starts[i]</c> — старт фракции i.</param>
/// <param name="SeedUsed">Сид, с которого карта реально сгенерировалась (после повторов).</param>
public sealed record MapResult(World World, IReadOnlyList<Hex> Starts, int SeedUsed);
