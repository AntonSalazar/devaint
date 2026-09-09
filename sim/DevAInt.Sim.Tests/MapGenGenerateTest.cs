using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты полной генерации <see cref="MapGen.Generate"/>: детерминизм, связность,
/// старты, призы, линки, работа на 2/4/8 фракций.
/// </summary>
public class MapGenGenerateTest
{
    /// <summary>Реальные правила.</summary>
    private static readonly Rules _rules = Rules.Load(DataDir.Path);

    /// <summary>Один сид — одна карта, бит в бит.</summary>
    [Fact]
    public void SameSeedSameMap()
    {
        MapResult a = MapGen.Generate(_rules, seed: 42, factions: 2);
        MapResult b = MapGen.Generate(_rules, seed: 42, factions: 2);

        Assert.Equal(a.SeedUsed, b.SeedUsed);
        Assert.Equal(a.World.Type.Raw.ToArray(), b.World.Type.Raw.ToArray());
        Assert.Equal(a.World.Patch.Raw.ToArray(), b.World.Patch.Raw.ToArray());
        Assert.Equal(a.Starts, b.Starts);
        Assert.Equal(a.World.Links.Count, b.World.Links.Count);
    }

    /// <summary>Разные сиды — разные карты.</summary>
    [Fact]
    public void DifferentSeedsDifferentMaps()
    {
        MapResult a = MapGen.Generate(_rules, 1, 2);
        MapResult b = MapGen.Generate(_rules, 2, 2);

        Assert.NotEqual(a.World.Type.Raw.ToArray(), b.World.Type.Raw.ToArray());
    }

    /// <summary>Размер карты — из таблицы по числу фракций; сид не меньше запрошенного.</summary>
    [Fact]
    public void SizeComesFromRules()
    {
        MapResult result = MapGen.Generate(_rules, 42, 2);
        MapSizeDef size = _rules.MapSizeFor(2);

        Assert.Equal(size.Width, result.World.Width);
        Assert.Equal(size.Height, result.World.Height);
        Assert.Equal(2, result.World.KnownBy.Length);
        Assert.True(result.SeedUsed >= 42);
    }

    /// <summary>Карта на 2, 4 и 8 фракций: все узлы связаны, стартов N, дистанция между стартами по таблице.</summary>
    /// <param name="factions">Число фракций.</param>
    /// <param name="seed">Сид.</param>
    [Theory]
    [InlineData(2, 42)]
    [InlineData(2, 7)]
    [InlineData(4, 42)]
    [InlineData(8, 42)]
    public void MapIsConnectedWithValidStarts(int factions, int seed)
    {
        MapResult result = MapGen.Generate(_rules, seed, factions);
        World world = result.World;
        MapSizeDef size = _rules.MapSizeFor(factions);

        Assert.Equal(factions, result.Starts.Count);
        Assert.Equal(factions, result.Starts.Distinct().Count());
        foreach (Hex start in result.Starts)
        {
            Assert.Equal(NodeType.Home, world.Type[start]);
        }
        for (int i = 0; i < factions; i++)
        {
            for (int j = i + 1; j < factions; j++)
            {
                Assert.True(result.Starts[i].DistanceTo(result.Starts[j]) >= size.MinStartDist, $"starts {i},{j} too close");
            }
        }

        int nodes = world.Nodes().Count();
        int reached = Search.Bfs(result.Starts[0], h => world.Neighbors(h).Where(world.IsNode)).Count();
        Assert.Equal(nodes, reached);
        Assert.InRange((float)nodes / (world.Width * world.Height), 0.2f, 0.55f);
    }

    /// <summary>Призы на месте: датацентров и заводов не меньше таблицы, роутеры есть.</summary>
    [Fact]
    public void PrizesArePlaced()
    {
        MapResult result = MapGen.Generate(_rules, 42, 2);
        World world = result.World;
        MapSizeDef size = _rules.MapSizeFor(2);
        int datacenterPatch = _rules.NodeTypes[NodeType.Server].BasePatch + _rules.Profiles["datacenter"].PatchBonus;

        int datacenterCores = world.Nodes().Count(h => world.Type[h] == NodeType.Server && world.Patch[h] == datacenterPatch);
        int controllers = world.Nodes().Count(h => world.Type[h] == NodeType.Controller);
        int routers = world.Nodes().Count(h => world.Type[h] == NodeType.Router);
        int servers = world.Nodes().Count(h => world.Type[h] == NodeType.Server);

        Assert.True(datacenterCores >= size.Datacenters, "datacenter cores");
        Assert.True(controllers >= size.Factories, "factory controllers");
        Assert.True(routers >= size.Clusters - 1, "at least a router per corridor");
        Assert.True(servers >= 2 + size.Datacenters, "office servers for each faction plus datacenters");
    }

    /// <summary>Линки: концы — узлы нужных типов, не соседи по сетке, без дублей; число — по таблице.</summary>
    [Fact]
    public void LinksFollowTable()
    {
        MapResult result = MapGen.Generate(_rules, 42, 2);
        World world = result.World;
        LinkCounts counts = _rules.MapSizeFor(2).Links;

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

    /// <summary>Backbone — между самыми дальними роутерами.</summary>
    [Fact]
    public void BackboneSpansFarthestRouters()
    {
        MapResult result = MapGen.Generate(_rules, 42, 2);
        World world = result.World;
        List<Hex> routers = [.. world.Nodes().Where(h => world.Type[h] == NodeType.Router)];
        int farthest = routers.SelectMany(a => routers.Select(b => a.DistanceTo(b))).Max();

        Link backbone = world.Links.First(l => l.Kind == LinkKind.Backbone);

        Assert.Equal(farthest, backbone.A.DistanceTo(backbone.B));
    }

    /// <summary>Генератор ничего не присваивает: владельцев и тумана нет — это дело партии.</summary>
    [Fact]
    public void GeneratorLeavesOwnershipToGame()
    {
        MapResult result = MapGen.Generate(_rules, 42, 2);

        Assert.All(result.World.Cells(), h => Assert.Equal(-1, result.World.Owner[h]));
        Assert.All(result.World.Cells(), h => Assert.False(result.World.KnownBy[0][h]));
    }
}
