using DevAInt.Sim.Stats;
using Godot;

/// <summary>Капсула сущности с носом по взгляду, кольцом под своей и полосой действия; позиция плавно идёт к новой за тик sim.</summary>
public partial class EntityView : Node3D
{
    /// <summary>Радиус капсулы: тело sim минус половина толщины стены и зазор, чтобы капсула не входила в стену и не ловила свет снаружи.</summary>
    public const float Radius = 0.3f - (ChunkMesh.WallThickness / 2) - 0.02f;

    /// <summary>Путь шейдера полосы прогресса.</summary>
    public const string ProgressShaderPath = "res://view/progress_bar.gdshader";

    /// <summary>Длительность выпада при ударе в секундах.</summary>
    public const double LungeSeconds = 0.15;

    /// <summary>Длительность красной вспышки при попадании в секундах.</summary>
    public const double FlashSeconds = 0.2;

    /// <summary>За сколько секунд существо появляется или исчезает при входе в видимость и выходе из неё.</summary>
    public const double FadeSeconds = 0.25;

    /// <summary>Рост капсулы.</summary>
    private const float Height = 1.7f;

    /// <summary>Наибольший сдвиг тела вперёд при выпаде в метрах.</summary>
    private const float LungeDistance = 0.25f;

    /// <summary>Цвет вспышки попадания.</summary>
    private static readonly Color _flashColor = new(1f, 0.15f, 0.1f);

    /// <summary>Общий материал полос прогресса; значение у каждой полосы своё.</summary>
    private static readonly ShaderMaterial _barMaterial = new() { Shader = GD.Load<Shader>(ProgressShaderPath) };

    /// <summary>Полоса прогресса действия над головой.</summary>
    private readonly MeshInstance3D _bar = new()
    {
        Name = "ActionBar",
        Mesh = new QuadMesh { Size = new Vector2(0.8f, 0.1f) },
        MaterialOverride = _barMaterial,
        Position = Vector3.Up * (Height + 0.35f),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Visible = false,
    };

    /// <summary>Видимая часть, которая поворачивается по взгляду и делает выпад: тело и нос.</summary>
    private readonly Node3D _visual = new() { Name = "Visual" };

    /// <summary>Материал тела, свой у каждой капсулы: на нём вспышка.</summary>
    private readonly StandardMaterial3D _material = new();

    /// <summary>Цвет тела вида.</summary>
    private Color _color;

    /// <summary>Направление взгляда по земле, единичное.</summary>
    private Vector3 _facing = Vector3.Back;

    /// <summary>Сколько секунд осталось выпаду.</summary>
    private double _lunge;

    /// <summary>Сколько секунд осталось вспышке.</summary>
    private double _flash;

    /// <summary>К чему идёт проявление: видно ли существо по видимости своего персонажа.</summary>
    private float _shownTarget = 1;

    /// <summary>Откуда идёт интерполяция.</summary>
    private Vector3 _from;

    /// <summary>Куда идёт интерполяция.</summary>
    private Vector3 _to;

    /// <summary>Доля пройденного пути движения от 0 до 1.</summary>
    private float _moveProgress = 1;

    /// <summary>Id сущности.</summary>
    public long EntityId { get; private set; }

    /// <summary>Доля выполнения действия на полосе, null если действия нет.</summary>
    public float? ActionProgress { get; private set; }

    /// <summary>Видна ли полоса прогресса.</summary>
    public bool ActionBarVisible => _bar.Visible;

    /// <summary>Своя ли сущность.</summary>
    public bool Own { get; private set; }

    /// <summary>Есть ли кольцо под капсулой: только у своей.</summary>
    public bool HasRing => GetNodeOrNull("Ring") is not null;

    /// <summary>Текущий цвет тела, со вспышкой.</summary>
    public Color BodyColor => _material.AlbedoColor;

    /// <summary>Направление взгляда по земле.</summary>
    public Vector3 Facing => _facing;

    /// <summary>Сдвиг тела вперёд от выпада.</summary>
    public float LungeOffset => _visual.Position.Length();

    /// <summary>Идёт ли вспышка попадания.</summary>
    public bool IsFlashing => _flash > 0;

    /// <summary>Насколько существо проявлено: 0 - скрыто, 1 - видно целиком.</summary>
    public float Shown { get; private set; } = 1;

    /// <summary>Видно ли существо своему персонажу: оно плавно появляется или исчезает.</summary>
    public void SetSeen(bool seen) => _shownTarget = seen ? 1 : 0;

    /// <summary>Настройка: id, своя ли сущность, начальная позиция и цвет вида.</summary>
    public void Init(long entityId, bool own, Vector3 position, Color color)
    {
        EntityId = entityId;
        Own = own;
        Name = $"Entity_{entityId}";
        _color = color;
        _material.AlbedoColor = color;
        _visual.AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = Radius, Height = Height },
            MaterialOverride = _material,
            Position = Vector3.Up * Height / 2,
        });

        // Нос на уровне глаз со стороны взгляда: капсула симметрична, а удар бьёт вперёд
        _visual.AddChild(new MeshInstance3D
        {
            Name = "Nose",
            Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.08f, 0.12f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color.Darkened(0.5f) },
            Position = new Vector3(0, Height * 0.8f, Radius),
        });
        AddChild(_visual);
        if (own)
        {
            AddChild(new MeshInstance3D
            {
                Name = "Ring",
                Mesh = new TorusMesh { InnerRadius = Radius + 0.04f, OuterRadius = Radius + 0.12f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1, 1, 1, 0.8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
                Position = Vector3.Up * 0.02f,
                Scale = new Vector3(1, 0.2f, 1),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        AddChild(_bar);
        Position = _from = _to = position;
    }

    /// <summary>Новая цель: интерполяция начинается с того места, где капсула сейчас; чужая поворачивается по движению.</summary>
    public void SetTarget(Vector3 target)
    {
        // Свой взгляд задаёт ввод, а движение у стены бывает вбок от него
        if (!Own)
        {
            SetFacing(target - Position);
        }

        _from = Position;
        _to = target;
        _moveProgress = 0;
    }

    /// <summary>Поворот взгляда по направлению на земле; слишком короткое направление не меняет взгляд.</summary>
    public void SetFacing(Vector3 direction)
    {
        Vector3 flat = new(direction.X, 0, direction.Z);
        if (flat.LengthSquared() < 1e-6f)
        {
            return;
        }

        _facing = flat.Normalized();
        _visual.Rotation = new Vector3(0, Mathf.Atan2(_facing.X, _facing.Z), 0);
    }

    /// <summary>Выпад вперёд: удар этой сущности прошёл.</summary>
    public void PlayAttack() => _lunge = LungeSeconds;

    /// <summary>Красная вспышка: по этой сущности попали.</summary>
    public void PlayHit()
    {
        _flash = FlashSeconds;
        _material.AlbedoColor = _flashColor;
    }

    /// <summary>Шаг интерполяции, выпада и вспышки на delta секунд.</summary>
    public void Advance(double delta)
    {
        _moveProgress = Mathf.Min(1, _moveProgress + (float)(delta / GameClock.RealSecondsPerTick));
        Position = _from.Lerp(_to, _moveProgress);

        // Выпад туда и обратно по полуволне синуса
        _lunge = Mathf.Max(0, _lunge - delta);
        float lunge = _lunge > 0 ? Mathf.Sin(Mathf.Pi * (float)(1 - (_lunge / LungeSeconds))) * LungeDistance : 0;
        _visual.Position = _facing * lunge;

        if (_flash > 0)
        {
            _flash = Mathf.Max(0, _flash - delta);
            _material.AlbedoColor = _flash > 0 ? _flashColor : _color;
        }

        // Прозрачность у всей видимой части и полосы действия; совсем скрытое не рисуется вовсе
        Shown = Mathf.MoveToward(Shown, _shownTarget, (float)(delta / FadeSeconds));
        Visible = Shown > 0;
        foreach (Node child in _visual.GetChildren())
        {
            ((GeometryInstance3D)child).Transparency = 1 - Shown;
        }
    }

    /// <summary>Полоса прогресса: доля от 0 до 1 или null, чтобы скрыть.</summary>
    public void SetActionProgress(float? progress)
    {
        ActionProgress = progress;
        _bar.Visible = progress is not null;
        if (progress is float value)
        {
            _bar.SetInstanceShaderParameter("progress", value);
        }
    }
}
