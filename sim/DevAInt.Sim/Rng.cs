using System;
using System.Collections.Generic;

namespace DevAInt.Sim;


/// <summary>
/// Класс рандомайза.
/// Причина, почему не из либы <see cref="Random"/>
/// - он не гарантирует одинаковые последовательности между версиями .NET,
/// поэтому нужна своя стабильная реализация.
/// </summary>
public sealed class Rng(int seed)
{
    /// <summary>
    /// Внутреннее состояние генератора.
    /// Читается, чтобы сохранить его в <see cref="RunState"/>, и записывается,
    /// чтобы продолжить ту же последовательность после загрузки сейва.
    /// </summary>
    public ulong State { get; set; } = ((ulong)(uint)seed + 1) * 0x9E3779B97F4A7C15UL; // Домножение на "золотое сечение" (2⁶⁴/φ).


    /// <summary>
    /// Статичный метод восстановления генератора из сохранённого состояния.
    /// Сид тут не важен: состояние сразу перезаписывается.
    /// </summary>
    /// <param name="state">Сохранённое состояние.</param>
    /// <returns>Вернет генератор, продолжающий ту же последовательность.</returns>
    public static Rng FromState(ulong state) => new(0) { State = state };


    /// <summary>
    /// Метод генерации следующего сырого числа.
    /// Алгоритм xorshift64*.
    /// </summary>
    /// <returns>Вернет сырое число.</returns>
    public ulong NextU64()
    {
        State ^= State >> 12;
        State ^= State << 25;
        State ^= State >> 27;
        // Домножение на финальный размешиватель.
        return State * 0x2545F4914F6CDD1DUL;
    }


    /// <summary>
    /// Метод генерации случайного int числа в диапазоне [0..max).
    /// </summary>
    /// <param name="maxExclusive">Граница диапазона.</param>
    /// <returns>Вернет число в диапазоне [0..max).</returns>
    public int Next(int maxExclusive)
    {
        // Проверим, что максимальное значение валидно.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        // 0..max-1.
        return (int)(NextU64() % (ulong)maxExclusive);
    }


    /// <summary>
    /// Метод генерации случайного float числа в диапазоне [0.0f..1.0f).
    /// </summary>
    /// <returns>Вернет число в диапазоне [0.0f..1.0f).</returns>
    public float NextFloat() => (NextU64() >> 40) * (1f / 16777216f);


    /// <summary>
    /// Метод возврата флага, сработала ли вероятность.
    /// </summary>
    /// <param name="p">Значение вероятности в диапазоне [0.0f, 1.0f].</param>
    /// <returns>
    /// Вернет флаг результата.
    /// Если шанс меньше-равно 0.0f - никогда.
    /// Если шанс больше-равно 1.0f - всегда.
    /// </returns>
    public bool Chance(float p) => NextFloat() < p;


    /// <summary>
    /// Метод возврата случайного элемента из списка.
    /// </summary>
    /// <param name="items">Список элементов.</param>
    /// <typeparam name="T">Тип списка.</typeparam>
    /// <returns>Случайный элемент, вычисленный по <see cref="Next(int)"/>.</returns>
    public T Pick<T>(IReadOnlyList<T> items) => items.Count == 0
        ? throw new ArgumentException("empty list detected", nameof(items))
        : items[Next(items.Count)];
}
