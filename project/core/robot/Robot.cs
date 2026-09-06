using System;
using System.Collections.Generic;

using Godot;


/// <summary>
/// Класс робота игрока.
/// Умеет двигаться в изометрии, "замораживаться" на паузе.
/// Потом обрастёт всякой телесной симуляцией.
/// </summary>
public partial class Robot : CharacterBody2D
{
    /// <summary>
    /// Скорость ходьбы в px/сек по-горизонтали.
    /// </summary>
    public const float WalkSpeed = 220.0f;

    /// <summary>
    /// Максимальный размер заряда батареи.
    /// </summary>
    public const float BatteryMax = 100.0f;

    /// <summary>
    /// Маска для локомоции. Всегда поднят хотя бы один из этих флагов.
    /// </summary>
    public const State LocomotionMask = State.Idle | State.Walk;


    /// <summary>
    /// Кэш состояний, чтоб не делать каждый раз Enum.GetValues.
    /// </summary>
    private static readonly State[] _allStates = [State.Idle, State.Walk];

    /// <summary>
    /// Секунды реального времени с прошлого игрового минутного тика.
    /// Необходимы для вычисления разряда батареи. Разбито по состояниям.
    /// </summary>
    private readonly Dictionary<State, float> _seconds = [];


    /// <summary>
    /// Битовая маска состояний.
    /// </summary>
    private State _flags = State.Idle;

    /// <summary>
    /// Ссылка на экземпляр таймера игрового времени.
    /// </summary>
    private GameClock? _clock = null;

    /// <summary>
    /// Ссылка на экземпляр сетки роя.
    /// </summary>
    private SignalGrid? _grid = null;

    /// <summary>
    /// Ссылка на экземпляр отрисовки излучения.
    /// Заполняется в <see cref="_Ready"/>.
    /// </summary>
    private Halo _halo = null!;


    /// <summary>
    /// Битовое перечисление состояний робота.
    /// </summary>
    [Flags]
    public enum State
    {
        /// <summary>
        /// Стоит на месте.
        /// </summary>
        Idle = 1 << 0,

        /// <summary>
        /// Ходьба.
        /// </summary>
        Walk = 1 << 1,
    }


    /// <summary>
    /// Суммарное излучение робота по его текущим состояниям <see cref="Flags"/>.
    /// </summary>
    public float Emission
    {
        get
        {
            float emission = 0.0f;
            foreach (State state in _allStates)
            {
                if (HasFlag(state))
                {
                    emission += EmissionOf(state);
                }
            }
            return emission;
        }
    }

    /// <summary>
    /// Битовая маска состояний. Смена маски перекрашивает ореол.
    /// </summary>
    public State Flags
    {
        get => _flags;
        internal set
        {
            if (_flags == value)
            {
                return;
            }

            _flags = value;
            _halo.SetEmission(Emission);
        }
    }

    /// <summary>
    /// Заряд батареи в %.
    /// </summary>
    public float Battery { get; private set; } = 0.0f;

    /// <summary>
    /// Процент тревоги роя по позиции робота.
    /// </summary>
    public float Notice => _grid?.GetNoticeAt(GlobalPosition) ?? 0.0f;

    /// <summary>
    /// Уровень тревоги роя по позиции робота.
    /// </summary>
    public SignalGrid.Level NoticeLevel =>
        _grid?.GetLevelAt(GlobalPosition) ?? SignalGrid.Level.None;


    /// <summary>
    /// Излучения состояния в ед/сек.
    /// </summary>
    /// <param name="state">Запрашиваемое состояние.</param>
    /// <returns>Излучение в ед/сек.</returns>
    public static float EmissionOf(State state) => state switch
    {
        State.Idle => 1.0f,
        State.Walk => 3.0f,
        _ => 0.0f,
    };


    /// <summary>
    /// Расход батареи.
    /// </summary>
    /// <param name="state">Запрашиваемое состояние.</param>
    /// <returns>Расход батареи от состояния.</returns>
    public static float DrainOf(State state) => state switch
    {
        State.Idle => 0.02f,
        State.Walk => 0.05f,
        _ => 0.0f,
    };


    /// <summary>
    /// Метод проверки содержится ли бит состояния в маске.
    /// </summary>
    /// <param name="flag">Бит состояния.</param>
    /// <returns>bool флаг</returns>
    public bool HasFlag(State flag) => (Flags & flag) != 0;


    /// <summary>
    /// Метод инициализации.
    /// </summary>
    /// <param name="clock">Ссылка на экземпляр таймера игрового времени.</param>
    /// <param name="grid">Ссылка на экземпляр сетки роя.</param>
    public void Init(GameClock clock, SignalGrid grid)
    {
        _clock = clock;
        _grid = grid;
        Battery = BatteryMax;
        _halo.SetEmission(Emission);
        EventBus.Subscribe<GameClock.OnMinutePassed>(OnMinutePassed);
        SetPhysicsProcess(true);
    }


    /// <summary>
    /// Метод деинициализации.
    /// </summary>
    public void Deinit()
    {
        SetPhysicsProcess(false);
        EventBus.Unsubscribe<GameClock.OnMinutePassed>(OnMinutePassed);
        Battery = 0.0f;
        _seconds.Clear();
        _grid = null;
        _clock = null;
    }


    /// <summary>
    /// Метод, вызываемый при первом кадре.
    /// </summary>
    public override void _Ready()
    {
        _halo = GetNode<Halo>("%Halo");
        SetPhysicsProcess(false);
    }


    /// <summary>
    /// Функция процессинга.
    /// </summary>
    /// <param name="delta">Фиксированное время между кадрами.</param>
    public override void _PhysicsProcess(double delta)
    {
        // Если время не задано или на паузе - пропускаем.
        if (_grid is null || _clock is null || _clock.IsPaused)
        {
            Velocity = Vector2.Zero;
            return;
        }

        // Делаем движение.
        Vector2 input = Input.GetVector(
            InputAction.Left, InputAction.Right,
            InputAction.Up, InputAction.Down
        );
        Velocity = Iso.MoveDirection(input) * WalkSpeed;
        MoveAndSlide();

        // Локомоция занимает свою часть маски, модификаторы сохраняются.
        State locomotion = Velocity.IsZeroApprox() ? State.Idle : State.Walk;
        Flags = (Flags & ~LocomotionMask) | locomotion;

        // Теперь копим секунды текущего состояния.
        float dt = (float)delta;
        foreach (State flag in _allStates)
        {
            if (HasFlag(flag))
            {
                _seconds[flag] = _seconds.GetValueOrDefault(flag) + dt;
            }
        }

        // Теперь вписываем свой след в память роя (игровые минуты из реального времени).
        _grid.Accumulate(
            GlobalPosition, Emission,
            (float)(delta * _clock.Speed / GameClock.MinuteDuration)
        );
    }


    /// <summary>
    /// Метод, вызываемый при получении сообщения
    /// о наступлении новой игровой минуты.
    /// </summary>
    /// <param name="message">Сообщение о новой игровой минуте.</param>
    private void OnMinutePassed(GameClock.OnMinutePassed message)
    {
        // Посчитаем общее время затраты.
        float total = 0.0f;
        foreach (float seconds in _seconds.Values)
        {
            total += seconds;
        }

        // Смотрим, что накопление > 0.0f.
        if (Mathf.IsZeroApprox(total) || total < 0.0f)
        {
            return;
        }

        // Определим общий расход батареи.
        float drain = 0.0f;
        foreach ((State state, float seconds) in _seconds)
        {
            drain += DrainOf(state) * (seconds / total);
        }
        Battery = Mathf.Clamp(Battery - drain, 0.0f, BatteryMax);

        // Готово, можно затирать расходы за пройденное время.
        _seconds.Clear();
    }


    /// <summary>
    /// Набор ключей для вектора движения по вводу.
    /// </summary>
    private static class InputAction
    {
        /// <summary>
        /// Движение влево.
        /// </summary>
        public static readonly StringName Left = "move_left";

        /// <summary>
        /// Движение вправо.
        /// </summary>
        public static readonly StringName Right = "move_right";

        /// <summary>
        /// Движение вверх.
        /// </summary>
        public static readonly StringName Up = "move_up";

        /// <summary>
        /// Движение вниз.
        /// </summary>
        public static readonly StringName Down = "move_down";
    }
}
