using Godot;


/// <summary>
/// Класс ореола излучения: пульсирующее кольца на земпле вокруг носителя.
/// Радиус и частота пульса растут с излучением и меняются плавно:
/// цель задается через <see cref="SetEmission"/>, текущие значения
/// сходятся к ней в <see cref="_Process"/>. Фаза пульса копится здесь же,
/// чтобы смена частоты не телепортировала кольцо.
/// </summary>
public partial class Halo : Polygon2D
{
    /// <summary>
    /// Базовый радиус ореола по горизонтали в px.
    /// </summary>
    public const float BaseRadius = 60.0f;

    /// <summary>
    /// Прирост радиуса на единицу излуения.
    /// </summary>
    public const float RadiusPerEmission = 30.0f;

    /// <summary>
    /// Базовая частота пульса, пульсов/сек.
    /// </summary>
    public const float BasePulse = 0.5f;

    /// <summary>
    /// Прирост частоты на единицу излучения.
    /// </summary>
    public const float PulsePerEmission = 0.5f;

    /// <summary>
    /// Скорость схождения радиуса к цели, px/сек.
    /// </summary>
    public const float RadiusSpeed = 240.0f;

    /// <summary>
    /// Скорость схождения частоты пульса к цели (пульсов/сек)/сек.
    /// </summary>
    public const float PulseRate = 2.0f;


    /// <summary>
    /// Целевая частота пульса, пульсов/сек.
    /// </summary>
    private float _targetPulseSpeed = BasePulse;

    /// <summary>
    /// Фаза пульса [0.0f, 1.0f).
    /// </summary>
    private float _phase = 0.0f;

    /// <summary>
    /// Радиус, под которым построен полигон (чтобы не перестраивать зря).
    /// </summary>
    private float _appliedRadius = -1.0f;


    /// <summary>
    /// Текущий радиус в px.
    /// </summary>
    public float Radius { get; private set; } = BaseRadius;

    /// <summary>
    /// Целевой радиус в px.
    /// </summary>
    public float TargetRadius { get; private set; } = BaseRadius;

    /// <summary>
    /// Текущая частота пульса, пульсов/сек.
    /// </summary>
    public float PulseSpeed { get; private set; } = BasePulse;


    /// <summary>
    /// Метод процессинга.
    /// </summary>
    /// <param name="delta">Время между кадрами.</param>
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Radius = Mathf.MoveToward(Radius, TargetRadius, RadiusSpeed * dt);
        PulseSpeed = Mathf.MoveToward(PulseSpeed, _targetPulseSpeed, PulseRate * dt);
        _phase = (_phase + (PulseSpeed * dt)) % 1.0f;
        Apply();
    }


    /// <summary>
    /// Метод установки излучения. Задает цель радиуса и пульса.
    /// Первый вызов (полигона ещё нет) применяется мгновенно, дальше - плавно.
    /// </summary>
    /// <param name="emission">Значение излучения.</param>
    public void SetEmission(float emission)
    {
        TargetRadius = BaseRadius + (RadiusPerEmission * emission);
        _targetPulseSpeed = BasePulse + (PulsePerEmission * emission);

        if (Polygon.Length == 0)
        {
            Radius = TargetRadius;
            PulseSpeed = _targetPulseSpeed;
        }
        Apply();
    }


    /// <summary>
    /// Метод применения текущих значений к полигону и шейдеру.
    /// </summary>
    private void Apply()
    {
        // Полигон перестраиваем только при смене радиуса.
        if (!Mathf.IsEqualApprox(_appliedRadius, Radius))
        {
            Vector2 half = new(Radius, Radius * Iso.ScaleY);
            Polygon = [-half, new(half.X, -half.Y), half, new(-half.X, half.Y)];
            _appliedRadius = Radius;
        }

        // Дальше обновим шейдер.
        if (Material is ShaderMaterial shader)
        {
            shader.SetShaderParameter(ShaderParam.Radius, Radius);
            shader.SetShaderParameter(ShaderParam.Phase, _phase);
            shader.SetShaderParameter(ShaderParam.IsoScaleY, Iso.ScaleY);
        }
    }

    /// <summary>
    /// Закэшированные имена параметров шейдера.
    /// </summary>
    private static class ShaderParam
    {
        public static readonly StringName Radius = "radius";
        public static readonly StringName Phase = "phase";
        public static readonly StringName IsoScaleY = "iso_scale_y";
    }
}
