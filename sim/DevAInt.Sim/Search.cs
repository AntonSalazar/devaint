using System;
using System.Collections.Generic;

namespace DevAInt.Sim;


/// <summary>
/// Класс обхода по гексам, не зависящие от того, что считать соседом.
/// </summary>
public static class Search
{
    /// <summary>
    /// Обход в ширину от <paramref name="start"/>.
    /// Выдает гексы волнами, каждый один раз, включая <paramref name="start"/>.
    /// Какие соседи допустимы - решает <paramref name="neighbors"/>.
    /// </summary>
    /// <param name="start">Откуда начинается поиск.</param>
    /// <param name="neighbors">Список соседей.</param>
    /// <returns>Вернет список гексов.</returns>
    public static IEnumerable<Hex> Bfs(Hex start, Func<Hex, IEnumerable<Hex>> neighbors)
    {
        Queue<Hex> queue = new();
        HashSet<Hex> seen = [start];
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Hex hex = queue.Dequeue();
            yield return hex;

            foreach (Hex next in neighbors(hex))
            {
                if (seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }
    }
}
