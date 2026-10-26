using DevAInt.Sim.Grid;
using Godot;

/// <summary>Ортографическая изометрическая камера, которая следует за точкой; с туманом войны и срезом стен.</summary>
public partial class IsoCamera : GameCamera
{
    /// <summary>Наклон изометрии: угол, под которым видна диагональ куба.</summary>
    public const float Pitch = 35.264f;

    /// <summary>Поворот вокруг вертикали.</summary>
    public const float Yaw = 45f;

    /// <summary>Расстояние до точки: на картинку ортографической камеры не влияет, но чем ближе, тем меньше объём для теней и точнее тени.</summary>
    public const float Distance = 20f;

    /// <summary>Видимая высота кадра в метрах при запуске.</summary>
    public const float DefaultSize = 18f;

    /// <summary>Самое близкое приближение.</summary>
    public const float MinSize = 8f;

    /// <summary>Самое дальнее отдаление: шире кадр - крупнее точка карты теней, дальше тонкие стены снова пропускают свет.</summary>
    public const float MaxSize = 30f;

    /// <summary>Во сколько раз меняется кадр за щелчок колёсика.</summary>
    public const float ZoomStep = 1.15f;

    /// <summary>Скорость догона цели, доля пути в секунду.</summary>
    private const float FollowSpeed = 8f;

    /// <summary>Точка, на которую смотрит камера сейчас.</summary>
    private Vector3 _focus;

    /// <summary>Затемнение по видимости и туман войны.</summary>
    public override bool UsesFog => true;

    /// <summary>Стены, закрывающие персонажа, срезаются.</summary>
    public override bool CutsWalls => true;

    /// <summary>Этажи выше своего не рисуются.</summary>
    public override bool HidesUpperFloors => true;

    /// <summary>Мышь свободна: прицел идёт за курсором.</summary>
    public override bool CapturesMouse => false;

    /// <summary>Настройка проекции и углов; вызывается сразу после создания.</summary>
    public void Init(Vector3 focus)
    {
        Name = "IsoCamera";
        Projection = ProjectionType.Orthogonal;
        Size = DefaultSize;
        Far = Distance * 3;
        RotationDegrees = new Vector3(-Pitch, Yaw, 0);
        SnapTo(focus);
    }

    /// <summary>Зум на steps щелчков: плюс - приближение, минус - отдаление, в пределах MinSize и MaxSize.</summary>
    public override void Zoom(int steps) => Size = Mathf.Clamp(Size / Mathf.Pow(ZoomStep, steps), MinSize, MaxSize);

    /// <summary>Мгновенный перенос на точку.</summary>
    public void SnapTo(Vector3 focus)
    {
        _focus = focus;
        Position = _focus + (Basis.Z * Distance);
    }

    /// <summary>Мгновенный перенос на ноги; взгляд изометрии не меняет.</summary>
    public override void Place(Vector3 feet, Vector3 facing) => SnapTo(feet);

    /// <summary>Плавное следование за ногами; стены и прицел изометрии не мешают.</summary>
    public override void Follow(Vector3 feet, World? world, bool aiming, double delta) => Follow(feet, delta);

    /// <summary>Прицел идёт через курсор мыши.</summary>
    public override Vector2 AimScreenPoint(Vector2 mouse, Vector2 screen) => mouse;

    /// <summary>Плавное следование за точкой за delta секунд.</summary>
    public void Follow(Vector3 target, double delta)
    {
        float weight = Mathf.Min(1, FollowSpeed * (float)delta);
        SnapTo(_focus.Lerp(target, weight));
    }
}
