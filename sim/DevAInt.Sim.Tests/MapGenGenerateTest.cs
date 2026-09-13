using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты полной генерации <see cref="MapGen.Generate"/>: детерминизм, связность,
/// старт и цель, призы, линки.
/// </summary>
public class MapGenGenerateTest
{
    /// <summary>Реальные правила.</summary>
    private static readonly Rules _rules = Rules.Load(DataDir.Path);

    /// <summary>Один сид — одна карта, бит в бит.</summary>
    [Fact]
    public void SameSeedSameMap()
    {
        MapResult a = MapGen.Generate(_rules, seed: 42);
        MapResult b = MapGen.Generate(_rules, seed: 42);

        Assert.Equal(a.SeedUsed, b.SeedUsed);
        Assert.Equal(a.World.Type.Raw.ToArray(), b.World.Type.Raw.ToArray());
        Assert.Equal(a.World.Patch.Raw.ToArray(), b.World.Patch.Raw.ToArray());
        Assert.Equal(a.Start, b.Start);
        Assert.Equal(a.Target, b.Target);
        Assert.Equal(a.World.Links.Count, b.World.Links.Count);
    }

    /// <summary>Разные сиды — разные карты (сиды берём далеко друг от друга: при неудаче генератор пробует seed + 1).</summary>
    [Fact]
    public void DifferentSeedsDifferentMaps()
    {
        MapResult a = MapGen.Generate(_rules, 42);
        MapResult b = MapGen.Generate(_rules, 4242);

        Assert.NotEqual(a.SeedUsed, b.SeedUsed);
        Assert.NotEqual(a.World.Type.Raw.ToArray(), b.World.Type.Raw.ToArray());
    }

    /// <summary>Размер карты — из таблицы; один слой тумана; сид не меньше запрошенного.</summary>
    [Fact]
    public void SizeComesFromRules()
    {
        MapResult result = MapGen.Generate(_rules, 42);
        MapSizeDef size = _rules.MapGen.Size;

        Assert.Equal(size.Width, result.World.Width);
        Assert.Equal(size.Height, result.World.Height);
        Assert.Single(result.World.KnownBy);
        Assert.True(result.SeedUsed >= 42);
    }

    /// <summary>На разных сидах: все узлы связаны, старт — Home, цель — сервер датацентра не ближе минимума.</summary>
    /// <param name="seed">Сид.</param>
    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(2026)]
    public void MapIsConnectedWithStartAndTarget(int seed)
    {
        MapResult result = MapGen.Generate(_rules, seed);
        World world = result.World;
        MapSizeDef size = _rules.MapGen.Size;
        int datacenterPatch = _rules.NodeTypes[NodeType.Server].BasePatch + _rules.Profiles["datacenter"].PatchBonus;

        Assert.Equal(NodeType.Home, world.Type[result.Start]);
        Assert.Equal(NodeType.Server, world.Type[result.Target]);
        Assert.Equal(datacenterPatch, world.Patch[result.Target]);
        Assert.True(result.Start.DistanceTo(result.Target) >= size.MinTargetDist, "target too close");

        int nodes = world.Nodes().Count();
        int reached = Search.Bfs(result.Start, h => world.Neighbors(h).Where(world.IsNode)).Count();
        Assert.Equal(nodes, reached);
        Assert.InRange((float)nodes / (world.Width * world.Height), 0.2f, 0.6f);
    }

    /// <summary>Призы на месте: датацентров и заводов не меньше таблицы, роутеры и офисные серверы есть.</summary>
    [Fact]
    public void PrizesArePlaced()
    {
        MapResult result = MapGen.Generate(_rules, 42);
        World world = result.World;
        MapSizeDef size = _rules.MapGen.Size;
        int datacenterPatch = _rules.NodeTypes[NodeType.Server].BasePatch + _rules.Profiles["datacenter"].PatchBonus;

        int datacenterCores = world.Nodes().Count(h => world.Type[h] == NodeType.Server && world.Patch[h] == datacenterPatch);
        int controllers = world.Nodes().Count(h => world.Type[h] == NodeType.Controller);
        int routers = world.Nodes().Count(h => world.Type[h] == NodeType.Router);
        int servers = world.Nodes().Count(h => world.Type[h] == NodeType.Server);

        Assert.True(datacenterCores >= size.Datacenters, "datacenter cores");
        Assert.True(controllers >= size.Factories, "factory controllers");
        Assert.True(routers >= size.Clusters - 1, "at least a router per corridor");
        Assert.True(servers > size.Datacenters, "at least one office server to compile on");
    }

    /// <summary>Линки: концы — узлы нужных типов, не соседи по сетке, без дублей; число — по таблице.</summary>
    [Fact]
    public void LinksFollowTable()
    {
        MapResult result = MapGen.Generate(_rules, 42);
        World world = result.World;
        LinkCounts counts = _rules.MapGen.Size.Links;

        Assert.Equal(counts.Backbone, world.Links.Count(l => l.Kind == LinkKind.Backbone));
        Assert.Equal(counts.Vpn, world.Links.Count(l => l.Kind == LinkKind.Vpn));
        Assert.Equal(counts.Sneakernet, world.Links.Count(l => l.Kind == LinkKind.Sneakernet));

        foreach (Link link in world.Links)
        {
            Assert.True(world.IsNode(link.A) && world.IsNode(link.B), "link ends are nodes");
            Assert.True(link.A.DistanceTo(link.B) >= 2, "link ends are not grid neighbors");
            (NodeType a, NodeType b) = (world.Type[link.A], world.Type[link.B]);
            switch (link.Kind)
            {
                case LinkKind.Backbone:
                    Assert.Equal((NodeType.Router, NodeType.Router), (a, b));
                    break;
                case LinkKind.Vpn:
                    Assert.Equal((NodeType.Server, NodeType.Server), (a, b));
                    break;
                case LinkKind.Sneakernet:
                    Assert.Equal((NodeType.Home, NodeType.Workstation), (a, b));
                    break;
                default:
                    break;
            }
        }

        HashSet<(Hex, Hex)> pairs = [];
        foreach (Link link in world.Links)
        {
            Assert.True(pairs.Add((link.A, link.B)) && pairs.Add((link.B, link.A)), "no duplicate links");
        }
    }

    /// <summary>Генератор ничего не присваивает: владельца и тумана нет — это дело забега.</summary>
    [Fact]
    public void GeneratorLeavesOwnershipToRun()
    {
        MapResult result = MapGen.Generate(_rules, 42);

        Assert.All(result.World.Cells(), h => Assert.Equal(-1, result.World.Owner[h]));
        Assert.All(result.World.Cells(), h => Assert.False(result.World.KnownBy[0][h]));
    }

    /// <summary>Дамп помечает старт и цель.</summary>
    [Fact]
    public void DumpMarksStartAndTarget()
    {
        MapResult result = MapGen.Generate(_rules, 42);

        string dump = MapGen.Dump(result.World, result.Start, result.Target);

        Assert.Equal(1, dump.Count(c => c == '*'));
        Assert.Equal(1, dump.Count(c => c == '$'));
        Assert.Equal(result.World.Height, dump.Count(c => c == '\n'));
    }
}
