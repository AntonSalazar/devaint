using DevAInt.Sim.Stats;
using Godot;

/// <summary>Капсула сущности с полосой прогресса действия; позиция плавно идёт от прошлой к новой за один тик sim.</summary>
public partial class EntityView : Node3D
{
    /// <summary>Радиус капсулы: тело sim минус половина толщины стены и зазор, чтобы капсула не входила в стену и не ловила свет снаружи.</summary>
    public const float Radius = 0.3f - (ChunkMesh.WallThickness / 2) - 0.02f;

    /// <summary>Путь шейдера полосы прогресса.</summary>
    public const string ProgressShaderPath = "res://view/progress_bar.gdshader";

    /// <summary>Рост капсулы.</summary>
    private const float Height = 1.7f;

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

    /// <summary>Настройка: id, своя ли сущность и начальная позиция.</summary>
    public void Init(long entityId, bool own, Vector3 position)
    {
        EntityId = entityId;
        Name = $"Entity_{entityId}";
        Color color = own ? new Color(0.25f, 0.55f, 1f) : new Color(1f, 0.55f, 0.2f);
        AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = Radius, Height = Height },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
            Position = Vector3.Up * Height / 2,
        });
        AddChild(_bar);
        Position = _from = _to = position;
    }

    /// <summary>Новая цель: интерполяция начинается с того места, где капсула сейчас.</summary>
    public void SetTarget(Vector3 target)
    {
        _from = Position;
        _to = target;
        _moveProgress = 0;
    }

    /// <summary>Шаг интерполяции на delta секунд.</summary>
    public void Advance(double delta)
    {
        _moveProgress = Mathf.Min(1, _moveProgress + (float)(delta / GameClock.RealSecondsPerTick));
        Position = _from.Lerp(_to, _moveProgress);
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
