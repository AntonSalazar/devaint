using DevAInt.Sim.Stats;
using Godot;

/// <summary>Капсула сущности; позиция плавно идёт от прошлой к новой за один тик sim.</summary>
public partial class EntityView : Node3D
{
    /// <summary>Рост капсулы.</summary>
    private const float Height = 1.7f;

    /// <summary>Откуда идёт интерполяция.</summary>
    private Vector3 _from;

    /// <summary>Куда идёт интерполяция.</summary>
    private Vector3 _to;

    /// <summary>Доля пройденного пути от 0 до 1.</summary>
    private float _progress = 1;

    /// <summary>Id сущности.</summary>
    public long EntityId { get; private set; }

    /// <summary>Настройка: id, своя ли сущность и начальная позиция.</summary>
    public void Init(long entityId, bool own, Vector3 position)
    {
        EntityId = entityId;
        Name = $"Entity_{entityId}";
        Color color = own ? new Color(0.25f, 0.55f, 1f) : new Color(1f, 0.55f, 0.2f);
        AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = Height },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
            Position = Vector3.Up * Height / 2,
        });
        Position = _from = _to = position;
    }

    /// <summary>Новая цель: интерполяция начинается с того места, где капсула сейчас.</summary>
    public void SetTarget(Vector3 target)
    {
        _from = Position;
        _to = target;
        _progress = 0;
    }

    /// <summary>Шаг интерполяции на delta секунд.</summary>
    public void Advance(double delta)
    {
        _progress = Mathf.Min(1, _progress + (float)(delta / GameClock.SecondsPerTick));
        Position = _from.Lerp(_to, _progress);
    }
}
