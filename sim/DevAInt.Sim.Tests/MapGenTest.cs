using System;
using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты генератора карты по частям: центры кластеров, взвешенный выбор,
/// заливка кластера, ASCII-дамп.
/// </summary>
public class MapGenTest
{
    /// <summary>Реальные правила.</summary>
    private static readonly Rules _rules = Rules.Load(DataDir.Path);

    /// <summary>Центры: нужное число, попарно не ближе minDist, с отступом от края.</summary>
    [Fact]
    public void CentersRespectDistanceAndInset()
    {
        Rng rng = new(42);

        List<Hex> centers = MapGen.PlaceCenters(rng, width: 24, height: 18, count: 7, minDist: 6, inset: 2);

        Assert.Equal(7, centers.Count);
        for (int i = 0; i < centers.Count; i++)
        {
            (int col, int row) = centers[i].ToOffset();
            Assert.InRange(col, 2, 21);
            Assert.InRange(row, 2, 15);
            for (int j = i + 1; j < centers.Count; j++)
            {
                Assert.True(centers[i].DistanceTo(centers[j]) >= 6, $"centers {i} and {j} too close");
            }
        }
    }

    /// <summary>Центры детерминированы сидом.</summary>
    [Fact]
    public void CentersAreDeterministic()
    {
        List<Hex> a = MapGen.PlaceCenters(new Rng(7), 24, 18, 7, 6, 2);
        List<Hex> b = MapGen.PlaceCenters(new Rng(7), 24, 18, 7, 6, 2);

        Assert.Equal(a, b);
    }

    /// <summary>Невыполнимое размещение — понятная ошибка, а не вечный цикл.</summary>
    [Fact]
    public void ImpossiblePlacementThrows() => Assert.Throws<InvalidOperationException>(() => MapGen.PlaceCenters(new Rng(1), 6, 6, 20, 5, 1));

    /// <summary>Взвешенный выбор: только ключи с весом > 0, доли примерно по весам.</summary>
    [Fact]
    public void WeightedPickFollowsWeights()
    {
        Rng rng = new(3);
        Dictionary<Os, float> weights = new() { [Os.Kestrel] = 0.7f, [Os.Bastion] = 0.3f, [Os.Forge] = 0f };
        Dictionary<Os, int> hits = [];

        for (int i = 0; i < 10_000; i++)
        {
            Os os = MapGen.PickWeighted(rng, weights);
            hits[os] = hits.GetValueOrDefault(os) + 1;
        }

        Assert.False(hits.ContainsKey(Os.Forge), "zero weight is never picked");
        Assert.InRange(hits[Os.Kestrel], 6_500, 7_500);
        Assert.InRange(hits[Os.Bastion], 2_500, 3_500);
    }

    /// <summary>Заливка: центр — тип из профиля с точным патчем, остальное в радиусе, типы и ОС по таблицам.</summary>
    [Fact]
    public void FillClusterFollowsProfile()
    {
        World world = new(24, 18, factions: 2);
        Rng rng = new(11);
        ClusterProfile profile = _rules.Profiles["datacenter"];
        Hex center = Hex.FromOffset(12, 9);

        MapGen.FillCluster(world, _rules, rng, center, profile, radius: 3);

        Assert.Equal(NodeType.Server, world.Type[center]);
        int expectedCenterPatch = _rules.NodeTypes[NodeType.Server].BasePatch + profile.PatchBonus;
        Assert.Equal(expectedCenterPatch, world.Patch[center]);

        Hex[] filled = [.. world.Nodes()];
        Assert.InRange(filled.Length, 20, 37);
        foreach (Hex hex in filled)
        {
            Assert.True(hex.DistanceTo(center) <= 3, "filled cell within radius");
            NodeType type = world.Type[hex];
            Assert.True(profile.NodeWeights.GetValueOrDefault(type) > 0, $"type {type} allowed by profile");
            Assert.True(_rules.NodeTypes[type].OsWeights.GetValueOrDefault(world.Os[hex]) > 0, "os allowed by node type");
            int basePatch = _rules.NodeTypes[type].BasePatch + profile.PatchBonus;
            Assert.InRange(world.Patch[hex], Math.Max(0, basePatch - 1), Math.Min(5, basePatch + 1));
            Assert.Equal(-1, world.Owner[hex]);
            Assert.NotEqual(string.Empty, world.Name[hex]);
        }
    }

    /// <summary>Заливка не выходит за карту и не трогает уже занятые гексы.</summary>
    [Fact]
    public void FillClusterClipsToMapAndKeepsExisting()
    {
        World world = new(10, 8, factions: 2);
        Hex corner = Hex.FromOffset(0, 0);
        Hex occupied = Hex.FromOffset(1, 1);
        world.Type[occupied] = NodeType.Router;

        MapGen.FillCluster(world, _rules, new Rng(5), corner, _rules.Profiles["residential"], radius: 3);

        Assert.Equal(NodeType.Router, world.Type[occupied]);
        Assert.NotEqual(NodeType.Empty, world.Type[corner]);
        Assert.All(world.Nodes(), h => Assert.True(h.DistanceTo(corner) <= 3));
    }

    /// <summary>Жилой профиль без Center: центр — обычный узел по весам, без дырки.</summary>
    [Fact]
    public void FillClusterWithoutCenterTypeStillFillsCenter()
    {
        World world = new(12, 12, factions: 2);
        Hex center = Hex.FromOffset(6, 6);

        MapGen.FillCluster(world, _rules, new Rng(9), center, _rules.Profiles["residential"], radius: 2);

        Assert.True(world.IsNode(center));
        Assert.Contains(world.Type[center], new[] { NodeType.Home, NodeType.IoT });
    }

    /// <summary>Остовные рёбра: n−1 для дерева плюс запрошенные лишние, без дублей, все центры связаны.</summary>
    [Fact]
    public void SpanningEdgesConnectEveryCenter()
    {
        List<Hex> centers = MapGen.PlaceCenters(new Rng(42), 26, 20, 7, 7, 2);

        List<(int A, int B)> edges = MapGen.SpanningEdges(centers, extraEdges: 2);

        Assert.Equal(6 + 2, edges.Count);
        Assert.Equal(edges.Count, edges.Select(e => (Math.Min(e.A, e.B), Math.Max(e.A, e.B))).Distinct().Count());
        Assert.All(edges, e => Assert.NotEqual(e.A, e.B));

        // Связность по рёбрам: волна от центра 0 доходит до всех.
        HashSet<int> reached = [0];
        Queue<int> queue = new();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            foreach ((int a, int b) in edges)
            {
                int other = a == cur ? b : b == cur ? a : -1;
                if (other >= 0 && reached.Add(other))
                {
                    queue.Enqueue(other);
                }
            }
        }
        Assert.Equal(centers.Count, reached.Count);
    }

    /// <summary>Дерево без лишних рёбер — ровно n−1, и оно минимальное: короче любого ребра, которое его заменит.</summary>
    [Fact]
    public void SpanningTreePrefersShortEdges()
    {
        List<Hex> centers = [new(0, 0), new(3, 0), new(6, 0), new(30, 0)];

        List<(int A, int B)> edges = MapGen.SpanningEdges(centers, extraEdges: 0);

        Assert.Equal(3, edges.Count);
        int total = edges.Sum(e => centers[e.A].DistanceTo(centers[e.B]));
        Assert.Equal(3 + 3 + 24, total);
    }

    /// <summary>Коридор соединяет два кластера роутерами и IoT, не трогая сами кластеры.</summary>
    [Fact]
    public void CorridorConnectsClusters()
    {
        World world = new(22, 9, factions: 2);
        Rng rng = new(8);
        Hex left = Hex.FromOffset(3, 4);
        Hex right = Hex.FromOffset(18, 4);
        MapGen.FillCluster(world, _rules, rng, left, _rules.Profiles["residential"], radius: 2);
        MapGen.FillCluster(world, _rules, rng, right, _rules.Profiles["office"], radius: 2);
        NodeType[] before = world.Type.Raw.ToArray();
        Assert.False(MapGen.Connected(world, left, right), "clusters start disconnected");

        MapGen.BuildCorridor(world, _rules, rng, left, right);

        Assert.True(MapGen.Connected(world, left, right), "corridor connects the clusters");
        List<Hex> corridor = [];
        foreach (Hex hex in world.Cells())
        {
            int index = hex.ToOffset().Row * world.Width + hex.ToOffset().Col;
            if (before[index] != NodeType.Empty)
            {
                Assert.Equal(before[index], world.Type[hex]);
            }
            else if (world.IsNode(hex))
            {
                corridor.Add(hex);
            }
        }
        Assert.NotEmpty(corridor);
        Assert.All(corridor, h => Assert.Contains(world.Type[h], new[] { NodeType.Router, NodeType.IoT }));
        Assert.Contains(corridor, h => world.Type[h] == NodeType.Router);
        Assert.All(corridor.Where(h => world.Type[h] == NodeType.Router), h => Assert.Equal(Os.Bastion, world.Os[h]));
        Assert.All(corridor, h => Assert.NotEqual(string.Empty, world.Name[h]));
    }

    /// <summary>Коридор начинается с роутера: первый пустой гекс после кластера — Router, дальше чередование.</summary>
    [Fact]
    public void CorridorStartsWithRouterAndAlternates()
    {
        World world = new(14, 3, factions: 2);
        Hex a = Hex.FromOffset(1, 1);
        Hex b = Hex.FromOffset(12, 1);
        world.Type[a] = NodeType.Home;
        world.Type[b] = NodeType.Home;

        MapGen.BuildCorridor(world, _rules, new Rng(2), a, b);

        Hex[] line = a.LineTo(b).ToArray();
        for (int i = 1; i < line.Length - 1; i++)
        {
            NodeType expected = (i - 1) % 2 == 0 ? NodeType.Router : NodeType.IoT;
            Assert.Equal(expected, world.Type[line[i]]);
        }
    }

    /// <summary>Connected — только по узлам и линкам; пустые гексы не проходимы, линк — проходим.</summary>
    [Fact]
    public void ConnectedUsesNodesAndLinks()
    {
        World world = new(8, 3, factions: 2);
        Hex a = Hex.FromOffset(0, 1);
        Hex b = Hex.FromOffset(7, 1);
        world.Type[a] = NodeType.Home;
        world.Type[b] = NodeType.Home;

        Assert.False(MapGen.Connected(world, a, b));
        world.Links.Add(new Link(a, b, LinkKind.Vpn));
        Assert.True(MapGen.Connected(world, a, b));
    }

    /// <summary>ASCII-дамп: строка на ряд, нечётные ряды со сдвигом, старты помечены.</summary>
    [Fact]
    public void DumpRendersRowsWithOffset()
    {
        World world = new(4, 3, factions: 2);
        world.Type[Hex.FromOffset(1, 0)] = NodeType.Home;
        world.Type[Hex.FromOffset(2, 1)] = NodeType.Server;
        Hex start = Hex.FromOffset(0, 2);
        world.Type[start] = NodeType.Home;

        string dump = MapGen.Dump(world, [start]);
        string[] lines = dump.TrimEnd('\n').Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Equal(". h . .", lines[0]);
        Assert.Equal(" . . S .", lines[1]);
        Assert.Equal("* . . .", lines[2]);
    }
}
