using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Тесты <see cref="World"/> на настоящей сцене world.tscn: позиция спавна, прямоугольник земли,
/// слой сигналов, сборка маршрутов и маркеров вышек из детей контейнеров.
/// </summary>
public class WorldTest : CsTestCase
{
    /// <summary>Сцена мира.</summary>
    private const string WorldScenePath = "res://core/world/world.tscn";

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
        CheckEq(world.RobotSpawn, marker.GlobalPosition, "spawn follows the marker");
        marker.Position += new Vector2(100.0f, 50.0f);
        CheckEq(world.RobotSpawn, marker.GlobalPosition, "spawn follows a moved marker");
    }

    /// <summary>Прямоугольник земли - охват закрашенных клеток карты.</summary>
    public void TestCellRectCoversPaintedMap()
    {
        World world = SpawnWorld();
        TileMapLayer ground = world.GetNode<TileMapLayer>("%Ground");
        CheckEq(world.CellRect, ground.GetUsedRect(), "rect equals the painted ground rect");
        CheckTrue(world.CellRect.Size.X > 0 && world.CellRect.Size.Y > 0, "map has painted cells");
    }

    /// <summary>Слой сигналов отдается тем же экземпляром, что в дереве.</summary>
    public void TestSignalLayerIsTheChild()
    {
        World world = SpawnWorld();
        CheckTrue(
            ReferenceEquals(world.SignalLayer, world.GetNode<SignalLayer>("%SignalLayer")),
            "SignalLayer is the %SignalLayer child");
    }

    /// <summary>Маршруты собираются из всех детей-PatrolPath контейнера Routes.</summary>
    public void TestPatrolRoutesBuiltFromPaths()
    {
        World world = SpawnWorld();
        int paths = world.GetNode<Node2D>("%Routes").GetChildren().OfType<PatrolPath>().Count();
        List<PatrolRoute> routes = world.GetPatrolRoutes();

        CheckTrue(paths >= 1, "scene has at least one patrol path");
        CheckEq(routes.Count, paths, "one route per PatrolPath child");
        CheckTrue(routes.All(static route => route.Cells.Count >= 1), "every route has cells");
    }

    /// <summary>Маркеры вышек - все дети-TowerMarker контейнера Towers.</summary>
    public void TestTowerMarkersCollected()
    {
        World world = SpawnWorld();
        int markers = world.GetNode<Node2D>("%Towers").GetChildren().OfType<TowerMarker>().Count();
        List<TowerMarker> towers = world.GetTowerMarkers();

        CheckTrue(markers >= 1, "scene has at least one tower marker");
        CheckEq(towers.Count, markers, "one entry per TowerMarker child");
        CheckTrue(towers.All(static tower => tower.Radius > 0.0f), "every tower has a positive radius");
    }

    /// <summary>Создание мира из сцены с добавлением в корень дерева (вход в дерево вызывает _Ready).</summary>
    /// <returns>Мир, освобождаемый в AfterEach.</returns>
    private World SpawnWorld()
    {
        World world = GD.Load<PackedScene>(WorldScenePath).Instantiate<World>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(world);
        _nodes.Add(world);
        return world;
    }
}
