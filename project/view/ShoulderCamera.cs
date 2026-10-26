using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Камера из-за правого плеча, как в Resident Evil 4: колёсико меняет расстояние, прицел приближает.</summary>
public partial class ShoulderCamera : MouseLookCamera
{
    /// <summary>Высота точки, вокруг которой вращается камера, над ногами: чуть ниже глаз.</summary>
    public const float PivotHeight = 1.6f;

    /// <summary>Сдвиг камеры вправо от точки вращения, чтобы персонаж не закрывал центр кадра.</summary>
    public const float ShoulderOffset = 0.45f;

    /// <summary>Расстояние до точки вращения при запуске.</summary>
    public const float DefaultDistance = 2.5f;

    /// <summary>Самое близкое расстояние колёсиком.</summary>
    public const float MinDistance = 1.5f;

    /// <summary>Самое дальнее расстояние колёсиком.</summary>
    public const float MaxDistance = 4f;

    /// <summary>Расстояние при зажатом прицеле.</summary>
    public const float AimDistance = 1f;

    /// <summary>Шаг расстояния за щелчок колёсика.</summary>
    public const float ZoomStep = 0.5f;

    /// <summary>Угол обзора по вертикали в градусах.</summary>
    public const float NormalFov = 70f;

    /// <summary>Угол обзора при зажатом прицеле.</summary>
    public const float AimFov = 45f;

    /// <summary>Зазор между камерой и стеной, в которую она упёрлась.</summary>
    public const float WallMargin = 0.2f;

    /// <summary>Дальность прорисовки: дальше всё уходит в туман.</summary>
    public const float FarDistance = 90f;

    /// <summary>За сколько секунд камера целиком переходит в прицел и обратно.</summary>
    private const float AimSeconds = 0.2f;

    /// <summary>Доля перехода в прицел: 0 - обычный вид, 1 - прицел.</summary>
    private float _aim;

    /// <summary>Расстояние колёсиком без прицела.</summary>
    public float Distance { get; private set; } = DefaultDistance;

    /// <summary>Самый низкий наклон: взгляд сверху на 60 градусов.</summary>
    protected override float MinPitch => Mathf.DegToRad(-60);

    /// <summary>Самый высокий наклон: взгляд снизу на 35 градусов.</summary>
    protected override float MaxPitch => Mathf.DegToRad(35);

    /// <summary>Точка камеры от точки вращения pivot до desired, которая не заходит за стену этажа z мира world.</summary>
    public static Vector3 KeepOutOfWalls(World? world, Vector3 pivot, Vector3 desired, int z)
    {
        if (world is null)
        {
            return desired;
        }

        // Стены считаются бесконечно высокими: камера из-за плеча всегда ниже их верха или рядом с ним.
        // Стена ищется на отрезке, продлённом на зазор: стена ровно на месте камеры тоже должна её остановить
        float length = new Vector2(desired.X - pivot.X, desired.Z - pivot.Z).Length();
        if (length < 1e-4f)
        {
            return desired;
        }

        float reach = length + WallMargin;
        Vector3 beyond = pivot + ((desired - pivot) * (reach / length));
        WorldPos from = new(pivot.X, pivot.Z, z);
        WorldPos to = new(beyond.X, beyond.Z, z);
        if (Sight.Blocked(world, from, to) is not double share)
        {
            return desired;
        }

        float allowed = Mathf.Clamp(((float)share * reach) - WallMargin, 0, length) / length;
        return pivot.Lerp(desired, allowed);
    }

    /// <summary>Настройка проекции; вызывается сразу после создания.</summary>
    public void Init()
    {
        Name = "ShoulderCamera";
        Projection = ProjectionType.Perspective;
        Fov = NormalFov;
        Far = FarDistance;
    }

    /// <summary>Камера встаёт сзади по взгляду.</summary>
    public override void Place(Vector3 feet, Vector3 facing)
    {
        LookAlong(facing);
        _aim = 0;
        Follow(feet, null, false, 0);
    }

    /// <summary>Камера за плечом на своём расстоянии, плавно в прицел и обратно, перед стеной останавливается.</summary>
    public override void Follow(Vector3 feet, World? world, bool aiming, double delta)
    {
        _aim = Mathf.MoveToward(_aim, aiming ? 1 : 0, (float)delta / AimSeconds);
        float distance = Mathf.Lerp(Distance, AimDistance, _aim);
        Fov = Mathf.Lerp(NormalFov, AimFov, _aim);
        Vector3 pivot = feet + (Vector3.Up * PivotHeight);
        Vector3 desired = pivot + (Basis.X * ShoulderOffset) + (Basis.Z * distance);
        int z = ViewMath.Level(feet.Y);
        Position = KeepOutOfWalls(world, pivot, desired, z);
    }

    /// <summary>Колёсико вперёд - ближе к плечу, назад - дальше.</summary>
    public override void Zoom(int steps) => Distance = Mathf.Clamp(Distance - (steps * ZoomStep), MinDistance, MaxDistance);
}
