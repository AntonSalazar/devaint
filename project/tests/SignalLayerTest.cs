using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="SignalLayer"/>: подгонка полигона под грид с полем, параметры шейдера,
/// текстура статики и ее обновление по OnStaticChanged, отписка.
/// Сцена signal_layer.tscn держит GDScript-версию, поэтому материал собирается руками.
/// </summary>
public class SignalLayerTest : CsTestCase
{
    /// <summary>Шейдер слоя из проекта.</summary>
    private const string LayerShaderPath = "res://core/signal_layer/signal_layer.gdshader";

    /// <summary>Угол тестового грида.</summary>
    private static readonly Vector2I _origin = new(-2, -2);

    /// <summary>Размер тестового грида.</summary>
    private static readonly Vector2I _size = new(5, 5);

    /// <summary>Узлы текущего теста - освобождаются в AfterEach.</summary>
    private readonly List<Node> _nodes = [];

    /// <summary>Полный сброс шины перед каждым тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Освобождение узлов и сброс шины после теста.</summary>
    public override void AfterEach()
    {
        foreach (Node node in _nodes)
        {
            node.Free();
        }
        _nodes.Clear();
        EventBus.Reset();
    }

    /// <summary>Полигон растянут на весь грид с полем в тайл: все центры клеток внутри.</summary>
    public void TestPolygonCoversGrid()
    {
        SignalGrid grid = new(_origin, _size);
        SignalLayer layer = SpawnLayer();
        layer.Init(grid);

        Vector2[] polygon = layer.Polygon;
        CheckEq(polygon.Length, 4, "polygon is a quad");
        int outside = 0;
        for (int x = _origin.X; x < _origin.X + _size.X; x++)
        {
            for (int y = _origin.Y; y < _origin.Y + _size.Y; y++)
            {
                Vector2 center = Iso.CellToWorld(new Vector2I(x, y));
                if (!Geometry2D.IsPointInPolygon(center, polygon))
                {
                    outside++;
                }
            }
        }
        CheckEq(outside, 0, "every cell center lies inside the polygon");

        // Поле вокруг грида: полигон шире прямоугольника углов ровно на TileWidth с каждой стороны.
        Rect2 corners = new(Iso.CellToWorld(_origin), Vector2.Zero);
        corners = corners.Expand(Iso.CellToWorld(_origin + new Vector2I(_size.X, 0)));
        corners = corners.Expand(Iso.CellToWorld(_origin + _size));
        corners = corners.Expand(Iso.CellToWorld(_origin + new Vector2I(0, _size.Y)));
        Rect2 bounds = new(polygon[0], Vector2.Zero);
        foreach (Vector2 point in polygon)
        {
            bounds = bounds.Expand(point);
        }
        CheckNear(bounds.Size.X, corners.Size.X + (2 * Iso.TileWidth), "polygon width has a tile margin");
        CheckNear(bounds.Size.Y, corners.Size.Y + (2 * Iso.TileWidth), "polygon height has a tile margin");
        layer.Deinit();
    }

    /// <summary>Шейдер получает геометрию грида и тайла из Iso.</summary>
    public void TestShaderGeometryParameters()
    {
        SignalGrid grid = new(_origin, _size);
        SignalLayer layer = SpawnLayer();
        layer.Init(grid);
        ShaderMaterial shader = (ShaderMaterial)layer.Material;

        CheckEq(shader.GetShaderParameter("grid_origin").AsVector2(), (Vector2)_origin, "grid_origin");
        CheckEq(shader.GetShaderParameter("grid_size").AsVector2(), (Vector2)_size, "grid_size");
        CheckEq(
            shader.GetShaderParameter("tile_size").AsVector2(),
            new Vector2(Iso.TileWidth, Iso.TileHeight),
            "tile_size comes from Iso");
        layer.Deinit();
    }

    /// <summary>Текстура статики размером с грид и обновляется по OnStaticChanged.</summary>
    public void TestCoverageTextureUpdates()
    {
        SignalGrid grid = new(_origin, _size);
        SignalLayer layer = SpawnLayer();
        layer.Init(grid);
        ShaderMaterial shader = (ShaderMaterial)layer.Material;

        ImageTexture? texture = shader.GetShaderParameter("coverage_tex").As<ImageTexture>();
        CheckTrue(texture is not null, "coverage_tex is an ImageTexture");
        if (texture is null)
        {
            layer.Deinit();
            return;
        }
        CheckEq(texture.GetSize(), (Vector2)_size, "texture size equals grid size");
        Vector2I local = Vector2I.Zero - _origin;
        CheckNear(texture.GetImage().GetPixel(local.X, local.Y).R, 0.0, "empty grid: pixel 0");

        grid.AddTower(Vector2I.Zero, 300.0f);
        texture = shader.GetShaderParameter("coverage_tex").As<ImageTexture>();
        CheckNear(
            texture.GetImage().GetPixel(local.X, local.Y).R, 1.0,
            "after OnStaticChanged the tower pixel is 1.0");
        layer.Deinit();
    }

    /// <summary>После Deinit слой не слушает шину: перестройка сети не трогает текстуру.</summary>
    public void TestDeinitUnsubscribes()
    {
        SignalGrid grid = new(_origin, _size);
        SignalLayer layer = SpawnLayer();
        layer.Init(grid);
        ShaderMaterial shader = (ShaderMaterial)layer.Material;
        ImageTexture? before = shader.GetShaderParameter("coverage_tex").As<ImageTexture>();
        layer.Deinit();

        grid.AddTower(Vector2I.Zero, 300.0f);
        ImageTexture? after = shader.GetShaderParameter("coverage_tex").As<ImageTexture>();
        CheckTrue(ReferenceEquals(before, after), "texture is not re-uploaded after Deinit");
    }

    /// <summary>Создание слоя с материалом на шейдере проекта (без добавления в дерево).</summary>
    /// <returns>Слой, освобождаемый в AfterEach.</returns>
    private SignalLayer SpawnLayer()
    {
        Shader shader = GD.Load<Shader>(LayerShaderPath);
        SignalLayer layer = new() { Material = new ShaderMaterial { Shader = shader } };
        _nodes.Add(layer);
        return layer;
    }
}
