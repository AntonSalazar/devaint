using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="World"/>: позиция спавна, прямоугольник земли, слой сигналов,
/// сборка маршрутов и маркеров вышек из детей контейнеров с фильтром по типу.
/// Сцена world.tscn держит GDScript-версии, поэтому дерево собирается руками из C#-нод.
/// </summary>
public class WorldTest : CsTestCase
{
    /// <summary>Тайлсет мира из проекта - для закраски клеток земли.</summary>
    private const string TileSetPath = "res://core/world/world_tiles.tres";

    /// <summary>Узлы текущего теста - освобождаются в AfterEach.</summary>
    private readonly List<Node> _nodes = [];

    /// <summary>Освобождение узлов после теста.</summary>
    public override void AfterEach()
    {
        foreach (Node node in _nodes)
        {
            node.Free();
        }
        _nodes.Clear();
    }

    /// <summary>Спавн робота - глобальная позиция маркера, а не мира или земли.</summary>
    public void TestRobotSpawnIsMarkerPosition()
    {
        World world = SpawnWorld();
        Marker2D marker = world.GetNode<Marker2D>("%RobotSpawn");
        marker.Position = new Vector2(100.0f, 50.0f);
        CheckEq(world.RobotSpawn, marker.GlobalPosition, "spawn follows the marker");
        CheckTrue(world.RobotSpawn != world.GlobalPosition, "spawn is not the world origin");
    }

    /// <summary>Прямоугольник земли - охват закрашенных клеток.</summary>
    public void TestCellRectCoversPaintedCells()
    {
        World world = SpawnWorld();
        TileMapLayer ground = world.GetNode<TileMapLayer>("%Ground");
        int sourceId = ground.TileSet.GetSourceId(0);
        ground.SetCell(new Vector2I(-2, -1), sourceId, Vector2I.Zero);
        ground.SetCell(new Vector2I(3, 4), sourceId, Vector2I.Zero);

        CheckEq(world.CellRect, new Rect2I(-2, -1, 6, 6), "rect spans painted cells");
    }

    /// <summary>Слой сигналов отдается тем же экземпляром, что в дереве.</summary>
    public void TestSignalLayerIsTheChild()
    {
        World world = SpawnWorld();
        CheckTrue(
            ReferenceEquals(world.SignalLayer, world.GetNode<SignalLayer>("%SignalLayer")),
            "SignalLayer is the %SignalLayer child");
    }

    /// <summary>Маршруты собираются только из детей-PatrolPath, чужие ноды пропускаются.</summary>
    public void TestPatrolRoutesBuiltFromPaths()
    {
        World world = SpawnWorld();
        List<PatrolRoute> routes = world.GetPatrolRoutes();

        CheckEq(routes.Count, 2, "two PatrolPath children give two routes");
        if (routes.Count == 2)
        {
            CheckNear(routes[0].Speed, 111.0, "first route keeps its speed");
            CheckNear(routes[1].Speed, 222.0, "second route keeps its speed");
            CheckEq(routes[0].Cells.Count, 2, "route cells come from the curve");
        }
    }

    /// <summary>Маркеры вышек - только дети-TowerMarker, обычные Marker2D пропускаются.</summary>
    public void TestTowerMarkersFiltered()
    {
        World world = SpawnWorld();
        List<TowerMarker> towers = world.GetTowerMarkers();

        CheckEq(towers.Count, 2, "two TowerMarker children");
        if (towers.Count == 2)
        {
            CheckNear(towers[0].Radius, 300.0, "first tower radius");
            CheckNear(towers[1].Radius, 450.0, "second tower radius");
        }
    }

    /// <summary>Путь патруля с двумя клетками кривой и заданной скоростью.</summary>
    /// <param name="speed">Скорость маршрута.</param>
    /// <param name="from">Первая клетка.</param>
    /// <param name="to">Вторая клетка.</param>
    /// <returns>Готовая нода PatrolPath.</returns>
    private static PatrolPath MakePath(float speed, Vector2I from, Vector2I to)
    {
        PatrolPath path = new() { Curve = new Curve2D(), Speed = speed };
        path.Curve.AddPoint(Iso.CellToWorld(from));
        path.Curve.AddPoint(Iso.CellToWorld(to));
        return path;
    }

    /// <summary>
    /// Сборка мира руками, как в world.tscn: земля с тайлсетом, слой, спавн,
    /// контейнеры маршрутов и вышек с детьми-приманками чужого типа.
    /// </summary>
    /// <returns>Мир в корне дерева, освобождаемый в AfterEach.</returns>
    private World SpawnWorld()
    {
        World world = new();
        TileMapLayer ground = new() { Name = "Ground", TileSet = GD.Load<TileSet>(TileSetPath) };
        SignalLayer signalLayer = new() { Name = "SignalLayer" };
        Marker2D spawn = new() { Name = "RobotSpawn" };
        Node2D routes = new() { Name = "Routes" };
        Node2D towers = new() { Name = "Towers" };

        routes.AddChild(MakePath(111.0f, new Vector2I(0, 0), new Vector2I(2, 0)));
        routes.AddChild(new Path2D { Name = "Decoy" });
        routes.AddChild(MakePath(222.0f, new Vector2I(1, 1), new Vector2I(1, 3)));
        towers.AddChild(new TowerMarker { Name = "Tower0", Radius = 300.0f });
        towers.AddChild(new Marker2D { Name = "Decoy" });
        towers.AddChild(new TowerMarker { Name = "Tower1", Radius = 450.0f });

        foreach (Node child in new Node[] { ground, signalLayer, spawn, routes, towers })
        {
            world.AddChild(child);
            child.Owner = world;
            child.UniqueNameInOwner = true;
        }

        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(world);
        _nodes.Add(world);
        return world;
    }
}
