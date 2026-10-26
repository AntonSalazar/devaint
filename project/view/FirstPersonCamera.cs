using DevAInt.Sim.Grid;
using Godot;

/// <summary>Камера от первого лица: в глазах своей капсулы, персонаж смотрит туда же, прицел сужает угол обзора.</summary>
public partial class FirstPersonCamera : MouseLookCamera
{
    /// <summary>Высота глаз над ногами.</summary>
    public const float EyeHeight = 1.6f;

    /// <summary>Угол обзора по вертикали в градусах.</summary>
    public const float NormalFov = 75f;

    /// <summary>Угол обзора при зажатом прицеле.</summary>
    public const float AimFov = 50f;

    /// <summary>За сколько секунд камера целиком переходит в прицел и обратно.</summary>
    private const float AimSeconds = 0.2f;

    /// <summary>Доля перехода в прицел.</summary>
    private float _aim;

    /// <summary>Своя капсула не рисуется: камера внутри неё.</summary>
    public override bool ShowsOwnBody => false;

    /// <summary>Персонаж смотрит туда же, куда камера.</summary>
    public override bool FacesAim => true;

    /// <summary>Почти вертикально вниз.</summary>
    protected override float MinPitch => Mathf.DegToRad(-80);

    /// <summary>Почти вертикально вверх.</summary>
    protected override float MaxPitch => Mathf.DegToRad(80);

    /// <summary>Настройка проекции; вызывается сразу после создания.</summary>
    public void Init()
    {
        Name = "FirstPersonCamera";
        Projection = ProjectionType.Perspective;
        Fov = NormalFov;
        Far = ShoulderCamera.FarDistance;
    }

    /// <summary>Камера в глазах, взгляд по взгляду капсулы.</summary>
    public override void Place(Vector3 feet, Vector3 facing)
    {
        LookAlong(facing);
        _aim = 0;
        Follow(feet, null, false, 0);
    }

    /// <summary>Камера в глазах, плавно в прицел и обратно.</summary>
    public override void Follow(Vector3 feet, World? world, bool aiming, double delta)
    {
        _aim = Mathf.MoveToward(_aim, aiming ? 1 : 0, (float)delta / AimSeconds);
        Fov = Mathf.Lerp(NormalFov, AimFov, _aim);
        Position = feet + (Vector3.Up * EyeHeight);
    }

    /// <summary>Колёсико от первого лица ничего не делает: приближает прицел.</summary>
    public override void Zoom(int steps)
    {
    }
}
