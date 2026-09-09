using System.Linq;

using DevAInt.Sim.Data;

using Xunit;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Тесты <see cref="World"/>: слои созданы по размеру, значения по умолчанию,
/// соседи в границах карты и через линки, перечисление узлов.
/// </summary>
public class WorldTest
{
    /// <summary>Все слои имеют размер карты; туман — по слою на фракцию.</summary>
    [Fact]
    public void LayersMatchSize()
    {
        World world = new(24, 18, factions: 3);

        Assert.Equal(24, world.Width);
        Assert.Equal(18, world.Height);
        Assert.Equal(24 * 18, world.Type.Raw.Length);
        Assert.Equal(24 * 18, world.Noise.Raw.Length);
        Assert.Equal(3, world.KnownBy.Length);
        Assert.Equal(24 * 18, world.KnownBy[2].Raw.Length);
        Assert.Empty(world.Links);
    }

    /// <summary>По умолчанию: всё Empty, ничьё (−1), без укреплений, имена пустые.</summary>
    [Fact]
    public void DefaultsAreEmptyAndNeutral()
    {
        World world = new(5, 4, factions: 2);
        Hex hex = Hex.FromOffset(2, 2);

        Assert.Equal(NodeType.Empty, world.Type[hex]);
        Assert.Equal(-1, world.Owner[hex]);
        Assert.Equal(0, world.Hardening[hex]);
        Assert.Equal(0f, world.Noise[hex]);
        Assert.False(world.KnownBy[0][hex]);
        Assert.Equal(string.Empty, world.Name[hex]);
        Assert.False(world.IsNode(hex));
    }

    /// <summary>Cells перечисляет каждый гекс карты ровно один раз.</summary>
    [Fact]
    public void CellsEnumerateWholeMap()
    {
        World world = new(6, 5, factions: 2);

        Hex[] cells = [.. world.Cells()];

        Assert.Equal(30, cells.Length);
        Assert.Equal(30, cells.Distinct().Count());
        Assert.All(cells, c => Assert.True(world.Type.Contains(c)));
    }

    /// <summary>Соседи угла — только те, что внутри карты.</summary>
    [Fact]
    public void NeighborsStayInsideMap()
    {
        World world = new(4, 3, factions: 2);

        Hex[] corner = [.. world.Neighbors(Hex.FromOffset(0, 0))];
        Hex[] middle = [.. world.Neighbors(Hex.FromOffset(1, 1))];

        Assert.Equal(2, corner.Length);
        Assert.Contains(Hex.FromOffset(1, 0), corner);
        Assert.Contains(Hex.FromOffset(0, 1), corner);
        Assert.Equal(6, middle.Length);
    }

    /// <summary>Концы линка — соседи друг друга в обе стороны.</summary>
    [Fact]
    public void LinksMakeEndpointsNeighbors()
    {
        World world = new(6, 5, factions: 2);
        Hex a = Hex.FromOffset(0, 0);
        Hex b = Hex.FromOffset(5, 4);
        world.Links.Add(new Link(a, b, LinkKind.Backbone));

        Assert.Contains(b, world.Neighbors(a));
        Assert.Contains(a, world.Neighbors(b));
        Assert.Equal(3, world.Neighbors(a).Count());
    }

    /// <summary>Nodes перечисляет только не-пустые гексы.</summary>
    [Fact]
    public void NodesSkipEmpty()
    {
        World world = new(4, 4, factions: 2);
        world.Type[Hex.FromOffset(1, 1)] = NodeType.Home;
        world.Type[Hex.FromOffset(2, 3)] = NodeType.Router;

        Hex[] nodes = [.. world.Nodes()];

        Assert.Equal(2, nodes.Length);
        Assert.True(world.IsNode(Hex.FromOffset(2, 3)));
        Assert.False(world.IsNode(Hex.FromOffset(0, 0)));
        Assert.False(world.IsNode(new Hex(0, -1)), "outside the map is not a node");
    }
}
