using Godot;

/// <summary>
/// Тесты <see cref="Iso"/>: изометрическая поправка движения, согласованность констант,
/// сверка конверсий клеток с настоящим TileMapLayer.
/// </summary>
public class IsoTest : CsTestCase
{
    /// <summary>Горизонтальный ввод - полная скорость, без искажений.</summary>
    public void TestHorizontalInputFullSpeed()
    {
        Vector2 direction = Iso.MoveDirection(Vector2.Right);
        CheckNear(direction.X, 1.0, "horizontal: x = 1.0");
        CheckNear(direction.Y, 0.0, "horizontal: y = 0.0");
    }

    /// <summary>Вертикальный ввод - вдвое короче горизонтального.</summary>
    public void TestVerticalInputHalfSpeed()
    {
        Vector2 direction = Iso.MoveDirection(Vector2.Down);
        CheckNear(direction.X, 0.0, "vertical: x = 0.0");
        CheckNear(direction.Y, Iso.ScaleY, "vertical: y = ScaleY");
        CheckNear(
            direction.Length() * 2.0, Iso.MoveDirection(Vector2.Right).Length(),
            "vertical speed is half of horizontal");
    }

    /// <summary>Диагональный ввод ложится вдоль ребра тайла 2:1.</summary>
    public void TestDiagonalInputFollowsTileEdge()
    {
        Vector2 direction = Iso.MoveDirection(Vector2.One);
        CheckNear(direction.Y / direction.X, 0.5, "diagonal slope is 2:1");

        Vector2 mirrored = Iso.MoveDirection(new Vector2(-1.0f, 1.0f));
        CheckNear(mirrored.Y / mirrored.X, -0.5, "mirrored diagonal slope is 2:1");
    }

    /// <summary>Нулевой ввод - нулевой вектор, без NaN.</summary>
    public void TestZeroInputIsSafe()
    {
        Vector2 direction = Iso.MoveDirection(Vector2.Zero);
        CheckTrue(direction == Vector2.Zero, "zero input gives a zero vector");
        CheckTrue(direction.IsFinite(), "no NaN in the result");
    }

    /// <summary>Константы согласованы: сжатие вертикали следует из пропорции тайла.</summary>
    public void TestConstantsAreConsistent()
    {
        CheckNear(
            (double)Iso.TileHeight / Iso.TileWidth, Iso.ScaleY,
            "ScaleY equals TileHeight / TileWidth");
    }

    /// <summary>Центры клеток бит-в-бит совпадают с TileMapLayer.MapToLocal.</summary>
    public void TestCellToWorldMatchesGodot()
    {
        // Нода не RefCounted: создали руками - обязаны освободить сами.
        TileMapLayer layer = MakeLayer();
        try
        {
            int mismatches = 0;
            for (int x = -12; x <= 12; x++)
            {
                for (int y = -12; y <= 12; y++)
                {
                    Vector2I cell = new(x, y);
                    if (Iso.CellToWorld(cell) != layer.MapToLocal(cell))
                    {
                        mismatches++;
                    }
                }
            }
            CheckEq(mismatches, 0, "CellToWorld matches MapToLocal on a 25x25 area");
        }
        finally
        {
            layer.Free();
        }
    }

    /// <summary>Точки внутри клеток попадают в ту же клетку, что и TileMapLayer.LocalToMap.</summary>
    public void TestWorldToCellMatchesGodot()
    {
        Vector2[] offsets =
        [
            Vector2.Zero,
            new(20.0f, 8.0f), new(-20.0f, 8.0f), new(20.0f, -8.0f), new(-20.0f, -8.0f),
            new(40.0f, 0.0f), new(-40.0f, 0.0f), new(0.0f, 20.0f), new(0.0f, -20.0f),
            new(30.0f, 14.0f), new(-33.0f, -12.0f), new(7.0f, -25.0f), new(-5.0f, 27.0f),
        ];
        TileMapLayer layer = MakeLayer();
        try
        {
            int mismatches = 0;
            for (int x = -12; x <= 12; x++)
            {
                for (int y = -12; y <= 12; y++)
                {
                    Vector2 center = Iso.CellToWorld(new Vector2I(x, y));
                    foreach (Vector2 offset in offsets)
                    {
                        Vector2 point = center + offset;
                        if (Iso.WorldToCell(point) != layer.LocalToMap(point))
                        {
                            mismatches++;
                        }
                    }
                }
            }
            CheckEq(mismatches, 0, "WorldToCell matches LocalToMap for interior points");
        }
        finally
        {
            layer.Free();
        }
    }

    /// <summary>Round-trip: клетка -> центр -> клетка без потерь.</summary>
    public void TestCellRoundTrip()
    {
        int mismatches = 0;
        for (int x = -50; x <= 50; x++)
        {
            for (int y = -50; y <= 50; y++)
            {
                Vector2I cell = new(x, y);
                if (Iso.WorldToCell(Iso.CellToWorld(cell)) != cell)
                {
                    mismatches++;
                }
            }
        }
        CheckEq(mismatches, 0, "WorldToCell(CellToWorld(c)) == c");
    }

    /// <summary>Расстояние по земле: горизонталь без изменений, экранная вертикаль разжата.</summary>
    public void TestGroundDistance()
    {
        CheckNear(
            Iso.GroundDistance(Vector2.Zero, new Vector2(100.0f, 0.0f)), 100.0,
            "horizontal ground distance equals screen distance");
        CheckNear(
            Iso.GroundDistance(Vector2.Zero, new Vector2(0.0f, 50.0f)), 100.0,
            "vertical screen distance is stretched by 1 / ScaleY");
        CheckNear(
            Iso.GroundDistance(new Vector2(10.0f, 10.0f), new Vector2(10.0f, 10.0f)), 0.0,
            "same point gives zero");
    }

    /// <summary>
    /// Конвенция Godot закреплена: клетка (0,0) занимает прямоугольник
    /// от (0,0) до (TileWidth, TileHeight).
    /// </summary>
    public void TestOriginCellConvention()
    {
        CheckEq(
            Iso.CellToWorld(Vector2I.Zero),
            new Vector2(Iso.TileWidth / 2.0f, Iso.TileHeight / 2.0f),
            "cell (0,0) center is at half tile size");
        CheckEq(
            Iso.WorldToCell(new Vector2(1.0f, 1.0f)), new Vector2I(-1, 0),
            "top-left corner belongs to (-1,0)");
    }

    /// <summary>Настоящий TileMapLayer с изометрией проекта для сверки конверсий.</summary>
    /// <returns>Слой, который вызывающий обязан освободить через Free().</returns>
    private static TileMapLayer MakeLayer()
    {
        TileSet tileSet = new()
        {
            TileShape = TileSet.TileShapeEnum.Isometric,
            TileLayout = TileSet.TileLayoutEnum.DiamondDown,
            TileSize = new Vector2I(Iso.TileWidth, Iso.TileHeight),
        };
        return new TileMapLayer { TileSet = tileSet };
    }
}
