using Godot;

/// <summary>Ортографическая изометрическая камера, которая следует за точкой.</summary>
public partial class IsoCamera : Camera3D
{
    /// <summary>Наклон изометрии: угол, под которым видна диагональ куба.</summary>
    public const float Pitch = 35.264f;

    /// <summary>Поворот вокруг вертикали.</summary>
    public const float Yaw = 45f;

    /// <summary>Расстояние до точки: на картинку ортографической камеры не влияет, но чем ближе, тем меньше объём для теней и точнее тени.</summary>
    public const float Distance = 20f;

    /// <summary>Скорость догона цели, доля пути в секунду.</summary>
    private const float FollowSpeed = 8f;

    /// <summary>Точка, на которую смотрит камера сейчас.</summary>
    private Vector3 _focus;

    /// <summary>Настройка проекции и углов; вызывается сразу после создания.</summary>
    public void Init(Vector3 focus)
    {
        Name = "IsoCamera";
        Projection = ProjectionType.Orthogonal;
        Size = 18f;
        Far = Distance * 3;
        RotationDegrees = new Vector3(-Pitch, Yaw, 0);
        SnapTo(focus);
    }

    /// <summary>Мгновенный перенос на точку.</summary>
    public void SnapTo(Vector3 focus)
    {
        _focus = focus;
        Position = _focus + (Basis.Z * Distance);
    }

    /// <summary>Плавное следование за точкой за delta секунд.</summary>
    public void Follow(Vector3 target, double delta)
    {
        float weight = Mathf.Min(1, FollowSpeed * (float)delta);
        SnapTo(_focus.Lerp(target, weight));
    }
}
