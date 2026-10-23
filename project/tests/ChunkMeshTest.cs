using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Тесты <see cref="ChunkMesh"/> на стартовой комнате core.</summary>
public class ChunkMeshTest : TestCase
{
    /// <summary>Допуск координат.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>Таблица полов и стен.</summary>
    private WorldTable _table = null!;

    /// <summary>Мир стартовой комнаты.</summary>
    private World _world = null!;

    /// <summary>Свежий мир перед каждым тестом.</summary>
    public override void BeforeEach()
    {
        ModLoadResult loaded = ModLoader.Load(ModsPath.Find(), ["core"]);
        _table = new WorldTable(loaded.Defs);
        _world = MapBuilder.Build(loaded.Defs.Get<MapDef>(GameRoot.MapId), _table);
    }

    /// <summary>Двор 10 на 10 - сто квадов по шесть вершин.</summary>
    public void TestFloorQuads()
    {
        ArrayMesh? floors = ChunkMesh.BuildFloors(_world, _table, 0, 0);

        Check(floors is not null, "floors exist");
        CheckEqual(600, floors!.SurfaceGetArrayLen(0), "floor vertices");
        CheckEqual(null, ChunkMesh.BuildFloors(_world, _table, 1, 1), "empty chunk has no floors");
    }

    /// <summary>Комната 6 на 5: 22 ребра стен, одно из них - закрытая дверь посреди южного ребра клетки 6, 8.</summary>
    public void TestRoomWalls()
    {
        List<WallInstance> walls = ChunkMesh.BuildWalls(_world, _table, 0, 0);
        WallInstance door = walls.Single(wall => wall.IsDoor);

        CheckEqual(22, walls.Count, "wall count");
        Check(!door.Open, "door is closed");
        CheckNear(6.5, door.Transform.Origin.X, Tolerance, "door x");
        CheckNear(9.0, door.Transform.Origin.Z, Tolerance, "door z");
        CheckNear(ChunkMesh.WallHeight / 2, door.Transform.Origin.Y, Tolerance, "door height center");
    }

    /// <summary>Открытая дверь повёрнута вокруг петли внутрь клетки и короче ребра.</summary>
    public void TestOpenDoorIsRotated()
    {
        CellPos doorCell = new(6, 8, 0);
        _world.SetEdge(doorCell, Side.South, _world.GetEdge(doorCell, Side.South) with { Open = true });

        WallInstance door = ChunkMesh.BuildWalls(_world, _table, 0, 0).Single(wall => wall.IsDoor);
        Vector3 along = door.Transform.Basis.X;

        Check(door.Open, "door is open");
        CheckNear(0.9, along.Length(), Tolerance, "open door length");
        Check(along.Z > 0.8, $"open door points into the yard, got {along}");
    }
}
