using Godot;

/// <summary>Перевод координат sim в Godot: x sim - X, y sim - Z, этаж - высота.</summary>
public static class ViewMath
{
    /// <summary>Высота этажа в метрах.</summary>
    public const float FloorHeight = 3f;

    /// <summary>Точка Godot для позиции sim.</summary>
    public static Vector3 ToGodot(float x, float y, int z) => new(x, z * FloorHeight, y);

    /// <summary>Этаж по высоте Godot: на лестнице ноги выше пола этажа, но ниже следующего.</summary>
    public static int Level(float height) => Mathf.FloorToInt((height / FloorHeight) + 0.01f);
}
