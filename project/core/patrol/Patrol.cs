using Godot;


/// <summary>
/// Класс патруля роя - активная сцена, следующая расписанию <see cref="PatrolRecord"/>.
/// Позицию не симулирует - ставит себя в <see cref="PatrolRecord.SampleAt"/> каждый кадр.
/// На стоянках водит конусом воcприятия по кругу ("сканирует").
/// </summary>
public partial class Patrol : Node2D
{
    /// <summary>
    /// Длина конуса восприятия, наземные px.
    /// </summary>
    public const float ConeLength = 300.0f;

    /// <summary>
    /// Полу-угол конуса, в радианах (~35 градусов).
    /// </summary>
    public const float ConeHalfAngle = 0.6f;

    /// <summary>
    /// Скорость обзора на стоянке, радиан за игровую минуту.
    /// </summary>
    public const float ScanRate = Mathf.Tau * 0.5f;

    /// <summary>
    /// Количество сегментов у конуса.
    /// </summary>
    private const int ConeSegments = 12;


    /// <summary>
    /// Ссылка на экземпляр таймера игрового времени.
    /// </summary>
    private GameClock? _clock;

    /// <summary>
    /// Ссылка на экземпляр записи расписания патрулирования.
    /// </summary>
    private PatrolRecord? _record;

    /// <summary>
    /// Ссылка на экземпляр пивота конуса.
    /// </summary>
    private Node2D _conePivot = null!;

    /// <summary>
    /// Ссылка на экземпляр отрисовки взгляда.
    /// </summary>
    private Polygon2D _cone = null!;


    /// <summary>
    /// Метод, вызываемый при первом кадре.
    /// </summary>
    public override void _Ready()
    {
        // Onready делаем.
        _conePivot = GetNode<Node2D>("%ConePivot");
        _cone = GetNode<Polygon2D>("%Cone");

        // Остановим процессинг.
        SetProcess(false);

        // Рисуем веер.
        Vector2[] points = new Vector2[ConeSegments + 2];
        points[0] = Vector2.Zero;
        for (int idx = 0; idx <= ConeSegments; idx++)
        {
            float angle = -ConeHalfAngle + (ConeHalfAngle * 2.0f * idx / ConeSegments);
            points[idx + 1] = Vector2.FromAngle(angle) * ConeLength;
        }
        _cone.Polygon = points;
    }


    /// <summary>
    /// Метод процессинга.
    /// </summary>
    /// <param name="delta">Время между кадрами.</param>
    public override void _Process(double delta)
    {
        // Защита от null.
        if (_clock is null || _record is null)
        {
            return;
        }

        double time = _clock.TimeMinutes;
        PatrolRecord.Sample sample = _record.SampleAt((float)time);
        GlobalPosition = sample.Position;

        // Взгляд: на ходу - по курсу, на стоянке - круговой обзор.
        _conePivot.Rotation = sample.Dwelling
            ? (float)Mathf.PosMod(time * ScanRate, Mathf.Tau)
            : GroundAngle(sample.Heading);
    }


    /// <summary>
    /// Метод инициализации.
    /// </summary>
    /// <param name="clock">Ссылка на экземпляр таймера игрового времени.</param>
    /// <param name="record">Ссылка на экземпляр записи расписания патрулирования.</param>
    public void Init(GameClock clock, PatrolRecord record)
    {
        _clock = clock;
        _record = record;
        SetProcess(true);
    }


    /// <summary>
    /// Метод деинициализации.
    /// </summary>
    public void Deinit()
    {
        SetProcess(false);
        _clock = null;
        _record = null;
    }


    /// <summary>
    /// Метод возврата наземного угла экранного направления.
    /// Изометрия разжимается, потому что pivot конуса сжимается по Y.
    /// </summary>
    /// <param name="heading">Направление взгляда.</param>
    /// <returns>Наземный угол экранного направления.</returns>
    private float GroundAngle(Vector2 heading) => new Vector2(heading.X, heading.Y / Iso.ScaleY).Angle();

}
