using Godot;


/// <summary>
/// Класс игрового времени.
/// Отвечает за тики симуляции.
/// Всё что завязано на игровое время - будет высчитываться тут.
/// </summary>
public class GameClock(int totalMinutes = 0)
{
    /// <summary>
    /// Длительность игрового дня в секундах реального времени.
    /// </summary>
    public const double DayDuration = 3600.0;

    /// <summary>
    /// Длительность игрового часа в секундах реального времени.
    /// </summary>
    public const double HourDuration = DayDuration / 24.0;

    /// <summary>
    /// Длительность игровой минуты в секундах реального времени.
    /// </summary>
    public const double MinuteDuration = HourDuration / 60.0;

    /// <summary>
    /// Количество минут в игровом дне.
    /// </summary>
    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Доступный набор множителей скорости.
    /// Во время геймплея будет доступно только пауза (x0) и x1.
    /// Остальные значения отведены для тестирования и отладки.
    /// </summary>
    public static readonly int[] Speeds = [0, 1, 2, 4, 8, 16, 32];

    /// <summary>
    /// Последний индекс не нулевой скорости.
    /// </summary>
    private int _lastSpeedId = 1;

    /// <summary>
    /// Аккумулятор времени. Вбирает в себя время в <see cref="Advance(double)"/>.
    /// </summary>
    private double _accumulator = 0.0;

    /// <summary>
    /// Общее игровое время в минутах.
    /// </summary>
    public int TotalMinutes { get; private set; } = totalMinutes;

    /// <summary>
    /// Индекс скорости из <see cref="Speeds"/>.
    /// </summary>
    public int SpeedId { get; private set; } = 1;

    /// <summary>
    /// Текущий множитель скорости.
    /// </summary>
    public int Speed { get; private set; } = 1;

    /// <summary>
    /// Флаг паузы вычислений.
    /// </summary>
    public bool IsPaused => Speed <= 0;

    /// <summary>
    /// Нормализованный прогресс игрового дня [0.0f, 1.0f).
    /// Пригодится шейдерам и всему тому, где нужно знать прогресс дня.
    /// </summary>
    public double DayProgress =>
        ((TotalMinutes % MinutesPerDay) + (_accumulator / MinuteDuration)) / MinutesPerDay;

    /// <summary>
    /// Непрерывное игровое время.
    /// На нём могут жить всякого рода расписания.
    /// </summary>
    public double TimeMinutes => TotalMinutes + (_accumulator / MinuteDuration);

    /// <summary>
    /// Текущий снимок игрового времени.
    /// </summary>
    public GameTime Datetime => ComputeTime(TotalMinutes);

    /// <summary>
    /// Текущий снимок игрового времени в виде строки.
    /// </summary>
    public string DatetimeStr
    {
        get
        {
            GameTime datetime = Datetime;
            return $"Day {datetime.Day} {datetime.Hour:D2}:{datetime.Minute:D2}";
        }
    }


    /// <summary>
    /// Метод вычисления игрового времени по общим игровым минутам.
    /// </summary>
    /// <param name="totalMinutes">Общие игровые минуты.</param>
    /// <returns>Вернет снимок-структуру <see cref="GameTime"/></returns>
    public static GameTime ComputeTime(int totalMinutes)
    {
        return new(
                Day: 1 + (totalMinutes / MinutesPerDay),
                Hour: totalMinutes / 60 % 24,
                Minute: totalMinutes % 60);
    }


    /// <summary>
    /// Метод установки скорости игрового времени по индексу.
    /// </summary>
    /// <param name="idx">Индекс скорости по <see cref="Speeds"/>.</param>
    public void SetSpeed(int idx)
    {
        SpeedId = Mathf.Clamp(idx, 0, Speeds.Length - 1);

        // Смотрим, сменилась ли скорость на новую.
        int speed = Speeds[SpeedId];
        if (Speed == speed)
        {
            return;
        }
        Speed = speed;
        GD.Print($"{this}: speed changed to x{speed}");
    }


    /// <summary>
    /// Метод смены скорости игрового времени по сдвигу.
    /// </summary>
    /// <param name="offset">Сдвиг в виде направления относительно текущего <see cref="SpeedId"/>.</param>
    public void ChangeSpeed(int offset) => SetSpeed(SpeedId + offset);


    /// <summary>
    /// Метод тумблера паузы.
    /// </summary>
    public void TogglePause()
    {
        // Вернем скорость, если пауза.
        if (IsPaused)
        {
            SetSpeed(_lastSpeedId);
            return;
        }

        // Ставим на паузу.
        _lastSpeedId = SpeedId;
        SetSpeed(0);
    }


    /// <summary>
    /// Метод добавления игрового времени по дельте.
    /// </summary>
    /// <param name="delta">Дельта от движка.</param>
    public void Advance(double delta)
    {
        // Копим время.
        _accumulator += delta * Speed;

        // Проводим тики.
        while (_accumulator >= MinuteDuration)
        {
            _accumulator -= MinuteDuration;

            // Сравним до и после.
            GameTime prevDt = Datetime;
            TotalMinutes += 1;
            GameTime crntDt = Datetime;

            // Минуты точно сменились.
            new OnMinutePassed(TotalMinutes, crntDt).Push();

            // Часы и дни уже будем сравнивать.
            if (prevDt.Hour != crntDt.Hour)
            {
                new OnHourPassed(TotalMinutes, crntDt).Push();
            }
            if (prevDt.Day != crntDt.Day)
            {
                new OnDayPassed(TotalMinutes, crntDt).Push();
            }
        }
    }


    /// <summary>
    /// Структура-снимок игрового времени.
    /// </summary>
    /// <param name="Day">Текущий номер игрового дня.</param>
    /// <param name="Hour">Текущий игровой час.</param>
    /// <param name="Minute">Текущая игровая минута.</param>
    public readonly record struct GameTime(int Day, int Hour, int Minute);


    /// <summary>
    /// Класс-контейнер с данными о текущем времени события.
    /// </summary>
    /// <param name="totalMinutes">Общее время в игровых минутах.</param>
    /// <param name="datetime">Снимок игрового времени.</param>
    public abstract class TickData(int totalMinutes, GameTime datetime) : EventBus.Message
    {
        /// <summary>
        /// Общее игровое время в игровых минутах.
        /// </summary>
        public int TotalMinutes { get; } = totalMinutes;

        /// <summary>
        /// Снимок игрового времени.
        /// </summary>
        public GameTime Datetime { get; } = datetime;
    }


    /// <summary>
    /// Класс-событие тика минуты игрового времени.
    /// </summary>
    /// <param name="totalMinutes">Общее время в игровых минутах.</param>
    /// <param name="datetime">Снимок игрового времени.</param>
    public class OnMinutePassed(int totalMinutes, GameTime datetime)
        : TickData(totalMinutes, datetime);


    /// <summary>
    /// Класс-событие тика часа игрового времени.
    /// </summary>
    /// <param name="totalMinutes">Общее время в игровых минутах.</param>
    /// <param name="datetime">Снимок игрового времени.</param>
    public class OnHourPassed(int totalMinutes, GameTime datetime)
        : TickData(totalMinutes, datetime);


    /// <summary>
    /// Класс-событие тика дня игрового времени.
    /// </summary>
    /// <param name="totalMinutes">Общее время в игровых минутах.</param>
    /// <param name="datetime">Снимок игрового времени.</param>
    public class OnDayPassed(int totalMinutes, GameTime datetime)
        : TickData(totalMinutes, datetime);


}
