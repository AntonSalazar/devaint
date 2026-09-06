using System.Collections.Generic;
using System.Linq;

using Godot;


/// <summary>
/// Класс игрового мира.
/// </summary>
public partial class World : Node2D
{
    /// <summary>
    /// Ссылка на экземпляр маркера, где будет спавниться робот <see cref="Robot"/>.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private Marker2D _robotSpawn = null!;

    /// <summary>
    /// Ссылка на экземпляр земли в виде <see cref="TileMapLayer"/>.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private TileMapLayer _ground = null!;

    /// <summary>
    /// Ссылка на контейнер с маршрутами <see cref="PatrolPath"/>.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private Node2D _routes = null!;

    /// <summary>
    /// Ссылка на контейнер с вышками <see cref="TowerMarker"/>.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private Node2D _towers = null!;


    /// <summary>
    /// Ссылка на экземпляр отрисовки сетки роя.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    public SignalLayer SignalLayer { get; private set; } = null!;

    /// <summary>
    /// Позиция спавна робота <see cref="Robot"/>.
    /// </summary>
    public Vector2 RobotSpawn => _robotSpawn.GlobalPosition;

    /// <summary>
    /// Прокрашенный прямоугольник земли.
    /// </summary>
    public Rect2I CellRect => _ground.GetUsedRect();


    /// <summary>
    /// Список маршрутов патрулей.
    /// </summary>
    public List<PatrolRoute> GetPatrolRoutes() =>
        [.. _routes.GetChildren().OfType<PatrolPath>().Select(static path => path.BuildRoute())];


    /// <summary>
    /// Список маркеров <see cref="TowerMarker"/>, где будут находиться вышки.
    /// </summary>
    public List<TowerMarker> GetTowerMarkers() =>
        [.. _towers.GetChildren().OfType<TowerMarker>()];


    /// <summary>
    /// Метод, вызываемый при первом кадре.
    /// </summary>
    public override void _Ready()
    {
        SignalLayer = GetNode<SignalLayer>("%SignalLayer");
        _robotSpawn = GetNode<Marker2D>("%RobotSpawn");
        _ground = GetNode<TileMapLayer>("%Ground");
        _routes = GetNode<Node2D>("%Routes");
        _towers = GetNode<Node2D>("%Towers");
    }
}
