using Godot;

/// <summary>Статичный класс математики изометрии.
/// Единая точка в коде проекта, где вычисляются мат. операции,
/// связанные с изометрией.</summary>
public static class Iso
{
    /// <summary>Ширина тайла в px.</summary>
    public const int TileWidth = 128;

    /// <summary>Высота тайла в px.</summary>
    public const int TileHeight = 64;

    /// <summary>Изометрический сдвиг по высоте в пропорции 2:1</summary>
    public const float ScaleY = 0.5f;


    /// <summary>Статичная функция возврата вектора скорости по вводу
    /// вводящей изометрическую поправку по вертикали.</summary>
    public static Vector2 MoveDirection(Vector2 input)
    {
        Vector2 direction = input.Normalized();
        direction.Y *= ScaleY;
        return direction;
    }

    /// <summary>Статичная функция возврата центра клетки в мировых координатах.
    /// Конвенция Godot для Diamond Down: клетка (0,0)
    /// лежит в прямоугольнике от (0,0) до (TileWidth, TileHeight).</summary>
    public static Vector2 CellToWorld(Vector2I cell)
    {
        float halfWidth = TileWidth * 0.5f;
        float halfHeight = TileHeight * 0.5f;
        return new Vector2(
            ((cell.X - cell.Y) * halfWidth) + halfWidth,
            ((cell.X + cell.Y) * halfHeight) + halfHeight
        );
    }

    /// <summary>Статичная функция возврата клетки,
    /// содержащей в себе мировую координату.
    /// На ребрах и вершинах ромбов - округление к ближайшему центру.</summary>
    public static Vector2I WorldToCell(Vector2 position)
    {
        float x = (position.X / TileWidth) + (position.Y / TileHeight) - 1.0f;
        float y = (position.Y / TileHeight) - (position.X / TileWidth);
        return new Vector2I(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
    }

    /// <summary>Статичная функция возврата расстояния между мировыми координатами,
    /// где вертикаль сжата по ScaleY.</summary>
    public static float GroundDistance(Vector2 a, Vector2 b)
    {
        Vector2 delta = b - a;
        delta.Y /= ScaleY;
        return delta.Length();
    }
}
