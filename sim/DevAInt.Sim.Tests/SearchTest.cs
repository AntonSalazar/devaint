using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты <see cref="Search.Bfs"/>: порядок волнами, каждый гекс один раз, ленивость.
/// </summary>
public class SearchTest
{
    /// <summary>Волна по всей плоскости с ограничением радиуса даёт диск: 1 + 6 + 12 гексов, по кольцам.</summary>
    [Fact]
    public void BfsVisitsDiskInRings()
    {
        Hex center = new(0, 0);

        Hex[] visited = [.. Search.Bfs(center, h => Neighbors(h).Where(n => center.DistanceTo(n) <= 2))];

        Assert.Equal(19, visited.Length);
        Assert.Equal(19, visited.Distinct().Count());
        Assert.Equal(center, visited[0]);
        Assert.All(visited.Skip(1).Take(6), h => Assert.Equal(1, center.DistanceTo(h)));
        Assert.All(visited.Skip(7), h => Assert.Equal(2, center.DistanceTo(h)));
    }

    /// <summary>Старт без соседей — только он сам.</summary>
    [Fact]
    public void BfsFromIsolatedStart()
    {
        Hex[] visited = [.. Search.Bfs(new Hex(3, 3), _ => [])];

        Assert.Equal([new Hex(3, 3)], visited);
    }

    /// <summary>Ленивость: Contains останавливает обход, дальше цели соседей не спрашивают.</summary>
    [Fact]
    public void BfsIsLazy()
    {
        Hex start = new(0, 0);
        Hex target = new(2, 0);
        List<Hex> asked = [];

        bool found = Search.Bfs(start, h =>
        {
            asked.Add(h);
            return Neighbors(h).Where(n => n.R == 0 && n.Q >= 0 && n.Q <= 10);
        }).Contains(target);

        Assert.True(found);
        Assert.DoesNotContain(new Hex(5, 0), asked);
    }

    /// <summary>Шесть соседей без ограничений.</summary>
    /// <param name="hex">Гекс.</param>
    /// <returns>Соседи.</returns>
    private static IEnumerable<Hex> Neighbors(Hex hex) => Enumerable.Range(0, 6).Select(hex.Neighbor);
}
