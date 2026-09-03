using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Тесты <see cref="GameClock"/>: тики минут/часов/дней, границы, скорость,
/// пауза, полезная нагрузка снимка времени, большие дельты.
/// </summary>
public class GameClockTest : CsTestCase
{
    /// <summary>Собранные тики текущего теста.</summary>
    private readonly List<GameClock.TickData> _ticks = [];

    /// <summary>Сброс шины и сборщика, подписка на все тики через базовый TickData.</summary>
    public override void BeforeEach()
    {
        EventBus.Reset();
        _ticks.Clear();
        EventBus.Subscribe<GameClock.TickData>(_ticks.Add);
    }

    /// <summary>Сброс шины после теста: статическое состояние не должно утекать.</summary>
    public override void AfterEach() => EventBus.Reset();

    /// <summary>Ровно 2.5 реальных секунды на скорости x1 - ровно одна минута.</summary>
    public void TestMinuteTickOnExactBoundary()
    {
        GameClock clock = new();

        clock.Advance(GameClock.MinuteDuration);
        CheckEq(_ticks.Count, 1, "exact minute boundary produces one tick");
        if (_ticks.Count == 1)
        {
            CheckTrue(_ticks[0] is GameClock.OnMinutePassed, "the tick is OnMinutePassed");
        }
    }

    /// <summary>Час реального времени (= игровые сутки) - 1440 минут, 24 часа, 1 день.</summary>
    public void TestFullDayTickCounts()
    {
        GameClock clock = new();

        clock.Advance(GameClock.DayDuration);
        CheckEq(CountOf<GameClock.OnMinutePassed>(), 1440, "1440 minute ticks per game day");
        CheckEq(CountOf<GameClock.OnHourPassed>(), 24, "24 hour ticks per game day");
        CheckEq(CountOf<GameClock.OnDayPassed>(), 1, "1 day tick per game day");
    }

    /// <summary>Порядок на границе суток: минута -> час -> день.</summary>
    public void TestTickOrderAtMidnight()
    {
        GameClock clock = new();

        clock.Advance(GameClock.DayDuration);
        string trace = string.Concat(_ticks.Select(tick => tick switch
        {
            GameClock.OnDayPassed => "d",
            GameClock.OnHourPassed => "h",
            _ => "m",
        }));
        CheckTrue(trace.EndsWith("mhd"), "midnight order is minute -> hour -> day");
    }

    /// <summary>Пауза (скорость x0) - тиков нет.</summary>
    public void TestPauseProducesNoTicks()
    {
        GameClock clock = new();

        clock.SetSpeed(0);
        clock.Advance(GameClock.DayDuration);
        CheckEq(_ticks.Count, 0, "no ticks while paused");
    }

    /// <summary>Скорость x2 - вдвое больше минут за то же реальное время.</summary>
    public void TestSpeedMultiplier()
    {
        GameClock clock = new();

        clock.SetSpeed(2);
        CheckEq(clock.Speed, 2, "speed index 2 gives multiplier x2");
        clock.Advance(GameClock.MinuteDuration);
        CheckEq(CountOf<GameClock.OnMinutePassed>(), 2, "x2 speed doubles the minute ticks");
    }

    /// <summary>Индекс скорости за пределами Speeds зажимается без падения.</summary>
    public void TestSetSpeedClamps()
    {
        GameClock clock = new();

        clock.SetSpeed(999);
        CheckEq(clock.Speed, GameClock.Speeds[^1], "index above range clamps to max");
        clock.SetSpeed(-5);
        CheckEq(clock.Speed, GameClock.Speeds[0], "index below range clamps to min");
    }

    /// <summary>Одна большая дельта не теряет тиков и идет по порядку.</summary>
    public void TestBigDeltaKeepsEveryTick()
    {
        GameClock clock = new();

        clock.Advance(GameClock.MinuteDuration * 15.0);
        CheckEq(CountOf<GameClock.OnMinutePassed>(), 15, "15 minutes in one big delta");
        bool ordered = !_ticks.Where((tick, idx) => tick.TotalMinutes != idx + 1).Any();
        CheckTrue(ordered, "TotalMinutes grows one by one");
    }

    /// <summary>Снимок времени в нагрузке: первая минута и граница часа.</summary>
    public void TestTickPayloadSnapshot()
    {
        GameClock clock = new();

        clock.Advance(GameClock.HourDuration + GameClock.MinuteDuration);
        if (_ticks.Count == 0)
        {
            Fail("no ticks collected");
            return;
        }

        GameClock.TickData first = _ticks[0];
        CheckEq(first.TotalMinutes, 1, "first tick: TotalMinutes = 1");
        CheckEq(first.Datetime, new GameClock.GameTime(Day: 1, Hour: 0, Minute: 1),
            "first tick: snapshot is day 1, 00:01");

        List<GameClock.TickData> hours =
            [.. _ticks.Where(tick => tick is GameClock.OnHourPassed)];
        CheckEq(hours.Count, 1, "one hour tick after 61 minutes");
        if (hours.Count == 1)
        {
            CheckEq(hours[0].Datetime.Hour, 1, "hour tick snapshot: hour = 1");
            CheckEq(hours[0].Datetime.Minute, 0, "hour tick snapshot: minute = 0");
        }
    }

    /// <summary>Смена суток: минута 1440 -> day 2, hour 0, minute 0.</summary>
    public void TestDayRolloverPayload()
    {
        GameClock clock = new();

        clock.Advance(GameClock.DayDuration + GameClock.MinuteDuration);
        List<GameClock.TickData> days =
            [.. _ticks.Where(tick => tick is GameClock.OnDayPassed)];
        CheckEq(days.Count, 1, "one day tick after 1441 minutes");
        if (days.Count == 1)
        {
            CheckEq(days[0].Datetime, new GameClock.GameTime(Day: 2, Hour: 0, Minute: 0),
                "day tick snapshot: day 2, 00:00");
        }
    }

    /// <summary>Недобор до границы минуты не тикает, добор - тикает.</summary>
    public void TestAccumulatorRemainder()
    {
        GameClock clock = new();

        clock.Advance(GameClock.MinuteDuration - 0.1);
        CheckEq(_ticks.Count, 0, "no tick before the minute boundary");
        clock.Advance(0.1);
        CheckEq(_ticks.Count, 1, "the tick fires once the boundary is reached");
    }

    /// <summary>Прогресс дня: полночь, 06:00, полдень.</summary>
    public void TestDayProgressBasics()
    {
        GameClock clock = new();

        CheckNear(clock.DayProgress, 0.0, "fresh clock: progress = 0.0");
        clock.Advance(GameClock.HourDuration * 6.0);
        CheckNear(clock.DayProgress, 0.25, "06:00: progress = 0.25");
        clock.Advance(GameClock.HourDuration * 6.0);
        CheckNear(clock.DayProgress, 0.5, "12:00: progress = 0.5");
    }

    /// <summary>Прогресс дня учитывает недобранную долю минуты из аккумулятора.</summary>
    public void TestDayProgressSubminuteFraction()
    {
        GameClock clock = new();

        clock.Advance(GameClock.MinuteDuration / 2.0);
        double expected = 0.5 / GameClock.MinutesPerDay;
        CheckNear(clock.DayProgress, expected, "half a minute adds its fraction");
    }

    /// <summary>Прогресс дня заворачивается на границе суток и не достигает 1.0.</summary>
    public void TestDayProgressWraps()
    {
        GameClock clock = new();

        clock.Advance(GameClock.DayDuration - GameClock.MinuteDuration);
        CheckTrue(clock.DayProgress < 1.0, "23:59: progress is below 1.0");
        clock.Advance(GameClock.MinuteDuration);
        CheckNear(clock.DayProgress, 0.0, "midnight: progress wraps to 0.0");
    }

    /// <summary>Прогресс дня замерзает на паузе.</summary>
    public void TestDayProgressFreezesOnPause()
    {
        GameClock clock = new();

        clock.Advance(GameClock.HourDuration);
        double before = clock.DayProgress;
        clock.SetSpeed(0);
        clock.Advance(GameClock.DayDuration);
        CheckNear(clock.DayProgress, before, "pause keeps the progress frozen");
    }

    /// <summary>Тумблер паузы запоминает и возвращает прежнюю скорость.</summary>
    public void TestTogglePause()
    {
        GameClock clock = new();

        clock.TogglePause();
        CheckEq(clock.Speed, 0, "toggle from x1 pauses");
        clock.TogglePause();
        CheckEq(clock.Speed, 1, "toggle again resumes to x1");

        clock.SetSpeed(3);
        clock.TogglePause();
        CheckEq(clock.Speed, 0, "toggle from x4 pauses");
        clock.TogglePause();
        CheckEq(clock.Speed, GameClock.Speeds[3], "resume restores x4, not x1");

        clock.SetSpeed(0);
        clock.TogglePause();
        CheckTrue(clock.Speed > 0, "toggle from a direct pause still resumes");
    }

    /// <summary>Стартовое время из конструктора учитывается временем и прогрессом.</summary>
    public void TestInitStartTime()
    {
        GameClock clock = new(12 * 60);

        CheckEq(clock.DatetimeStr, "Day 1 12:00", "start at noon of day 1");
        CheckNear(clock.DayProgress, 0.5, "noon start: progress = 0.5");

        GameClock defaultClock = new();
        CheckEq(defaultClock.DatetimeStr, "Day 1 00:00", "default start is midnight");
    }

    /// <summary>Границы ComputeTime: старт, предполночь, полночь второго дня.</summary>
    public void TestComputeTimeBounds()
    {
        CheckEq(GameClock.ComputeTime(0), new GameClock.GameTime(Day: 1, Hour: 0, Minute: 0),
            "minute 0 is day 1, 00:00");
        CheckEq(GameClock.ComputeTime(1439), new GameClock.GameTime(Day: 1, Hour: 23, Minute: 59),
            "minute 1439 is day 1, 23:59");
        CheckEq(GameClock.ComputeTime(1440), new GameClock.GameTime(Day: 2, Hour: 0, Minute: 0),
            "minute 1440 is day 2, 00:00");
    }

    /// <summary>Количество тиков типа <typeparamref name="T"/> в собранных.</summary>
    /// <typeparam name="T">Тип тика.</typeparam>
    /// <returns>Число тиков указанного типа.</returns>
    private int CountOf<T>() where T : GameClock.TickData =>
        _ticks.Count(tick => tick is T);
}
