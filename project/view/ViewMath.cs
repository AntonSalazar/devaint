using Godot;

/// <summary>Перевод координат sim в Godot: x sim - X, y sim - Z, этаж - высота.</summary>
public static class ViewMath
{
    /// <summary>Высота этажа в метрах.</summary>
    public const float FloorHeight = 3f;

    /// <summary>Точка Godot для позиции sim.</summary>
    public static Vector3 ToGodot(float x, float y, int z) => new(x, z * FloorHeight, y);
}
