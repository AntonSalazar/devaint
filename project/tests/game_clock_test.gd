extends TestCase

## Тесты [GameClock]: тики минут/часов/дней, границы, скорость, пауза,
## полезная нагрузка снимка времени, большие дельты.

#region FUNCTIONS
#region OVERRIDE_PUBLIC

## Полный сброс шины перед каждым тестом.
func before_each() -> void:
	EventBus.reset()


## Сброс шины после теста (статическая _ref не должна пережить тест).
func after_each() -> void:
	EventBus.reset()

#endregion

#region TESTS

## Ровно 2.5 реальных секунды на скорости x1 — ровно одна минута.
func test_minute_tick_on_exact_boundary() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.MINUTE_DURATION)
	check_eq(ticks.size(), 1, "exact minute boundary produces one tick")
	if ticks.size() == 1:
		check_true(ticks[0] is GameClock.OnMinutePassed, "the tick is OnMinutePassed")


## Час реального времени (= игровые сутки) — 1440 минут, 24 часа, 1 день.
func test_full_day_tick_counts() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.DAY_DURATION)
	check_eq(_count_minutes(ticks), 1440, "1440 minute ticks per game day")
	check_eq(_count_hours(ticks), 24, "24 hour ticks per game day")
	check_eq(_count_days(ticks), 1, "1 day tick per game day")


## Порядок на границе суток: минута -> час -> день.
func test_tick_order_at_midnight() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.DAY_DURATION)
	var trace: String = ""
	for tick: GameClock.TickData in ticks:
		if tick is GameClock.OnDayPassed:
			trace += "d"
		elif tick is GameClock.OnHourPassed:
			trace += "h"
		elif tick is GameClock.OnMinutePassed:
			trace += "m"
	check_true(trace.ends_with("mhd"), "midnight order is minute -> hour -> day")


## Пауза (скорость x0) — тиков нет.
func test_pause_produces_no_ticks() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.set_speed(0)
	clock.advance(GameClock.DAY_DURATION)
	check_eq(ticks.size(), 0, "no ticks while paused")


## Скорость x2 — вдвое больше минут за то же реальное время.
func test_speed_multiplier() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.set_speed(2)
	check_eq(clock.get_speed(), 2, "speed index 2 gives multiplier x2")
	clock.advance(GameClock.MINUTE_DURATION)
	check_eq(_count_minutes(ticks), 2, "x2 speed doubles the minute ticks")


## Индекс скорости за пределами SPEEDS зажимается без падения.
func test_set_speed_clamps() -> void:
	var clock: GameClock = GameClock.new()
	
	clock.set_speed(999)
	check_eq(clock.get_speed(), GameClock.SPEEDS[-1], "index above range clamps to max")
	clock.set_speed(-5)
	check_eq(clock.get_speed(), GameClock.SPEEDS[0], "index below range clamps to min")


## Одна большая дельта не теряет тиков и идет по порядку.
func test_big_delta_keeps_every_tick() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.MINUTE_DURATION * 15.0)
	check_eq(_count_minutes(ticks), 15, "15 minutes in one big delta")
	var expected: int = 0
	var ordered: bool = true
	for tick: GameClock.TickData in ticks:
		expected += 1
		if tick.total_minutes != expected:
			ordered = false
	check_true(ordered, "total_minutes grows one by one")


## Снимок времени в нагрузке: первая минута и граница часа.
func test_tick_payload_snapshot() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.HOUR_DURATION + GameClock.MINUTE_DURATION)
	if ticks.is_empty():
		fail("no ticks collected")
		return
	var first: GameClock.TickData = ticks[0]
	check_eq(first.total_minutes, 1, "first tick: total_minutes = 1")
	check_eq(first.day, 1, "first tick: day = 1")
	check_eq(first.hour, 0, "first tick: hour = 0")
	check_eq(first.minute, 1, "first tick: minute = 1")
	
	var hours: Array[GameClock.TickData] = []
	for tick: GameClock.TickData in ticks:
		if tick is GameClock.OnHourPassed:
			hours.append(tick)
	check_eq(hours.size(), 1, "one hour tick after 61 minutes")
	if hours.size() == 1:
		check_eq(hours[0].hour, 1, "hour tick snapshot: hour = 1")
		check_eq(hours[0].minute, 0, "hour tick snapshot: minute = 0")


## Смена суток: минута 1440 -> day 2, hour 0, minute 0.
func test_day_rollover_payload() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.DAY_DURATION + GameClock.MINUTE_DURATION)
	var days: Array[GameClock.TickData] = []
	for tick: GameClock.TickData in ticks:
		if tick is GameClock.OnDayPassed:
			days.append(tick)
	check_eq(days.size(), 1, "one day tick after 1441 minutes")
	if days.size() == 1:
		check_eq(days[0].day, 2, "day tick snapshot: day = 2")
		check_eq(days[0].hour, 0, "day tick snapshot: hour = 0")
		check_eq(days[0].minute, 0, "day tick snapshot: minute = 0")


## Недобор до границы минуты не тикает, добор — тикает.
func test_accumulator_remainder() -> void:
	var ticks: Array[GameClock.TickData] = _collect_ticks()
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.MINUTE_DURATION - 0.1)
	check_eq(ticks.size(), 0, "no tick before the minute boundary")
	clock.advance(0.1)
	check_eq(ticks.size(), 1, "the tick fires once the boundary is reached")


## Прогресс дня: полночь, 06:00, полдень.
func test_day_progress_basics() -> void:
	var clock: GameClock = GameClock.new()
	
	check_near(clock.get_day_progress(), 0.0, "fresh clock: progress = 0.0")
	clock.advance(GameClock.HOUR_DURATION * 6.0)
	check_near(clock.get_day_progress(), 0.25, "06:00: progress = 0.25")
	clock.advance(GameClock.HOUR_DURATION * 6.0)
	check_near(clock.get_day_progress(), 0.5, "12:00: progress = 0.5")


## Прогресс дня учитывает недобранную долю минуты из аккумулятора.
func test_day_progress_subminute_fraction() -> void:
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.MINUTE_DURATION / 2.0)
	var expected: float = 0.5 / GameClock.MINUTES_PER_DAY
	check_near(clock.get_day_progress(), expected, "half a minute adds its fraction")


## Прогресс дня заворачивается на границе суток и не достигает 1.0.
func test_day_progress_wraps() -> void:
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.DAY_DURATION - GameClock.MINUTE_DURATION)
	check_true(clock.get_day_progress() < 1.0, "23:59: progress is below 1.0")
	clock.advance(GameClock.MINUTE_DURATION)
	check_near(clock.get_day_progress(), 0.0, "midnight: progress wraps to 0.0")


## Прогресс дня замерзает на паузе.
func test_day_progress_freezes_on_pause() -> void:
	var clock: GameClock = GameClock.new()
	
	clock.advance(GameClock.HOUR_DURATION)
	var before: float = clock.get_day_progress()
	clock.set_speed(0)
	clock.advance(GameClock.DAY_DURATION)
	check_near(clock.get_day_progress(), before, "pause keeps the progress frozen")


## Стартовое время из конструктора учитывается временем и прогрессом.
func test_init_start_time() -> void:
	var clock: GameClock = GameClock.new(12 * 60)
	
	check_eq(clock.get_datetime_str(), "Day 1 12:00", "start at noon of day 1")
	check_near(clock.get_day_progress(), 0.5, "noon start: progress = 0.5")
	
	var default_clock: GameClock = GameClock.new()
	check_eq(default_clock.get_datetime_str(), "Day 1 00:00", "default start is midnight")

#endregion

#region REGULAR_PRIVATE

## Подписка-сборщик всех тиков часов через базовый [GameClock.TickData].
func _collect_ticks() -> Array[GameClock.TickData]:
	var ticks: Array[GameClock.TickData] = []
	var collector: Callable = func(msg: GameClock.TickData) -> void:
		ticks.append(msg)
	EventBus.subscribe(GameClock.TickData, collector, 0)
	return ticks


## Количество минутных тиков в [param ticks].
func _count_minutes(ticks: Array[GameClock.TickData]) -> int:
	var count: int = 0
	for tick: GameClock.TickData in ticks:
		if tick is GameClock.OnMinutePassed:
			count += 1
	return count


## Количество часовых тиков в [param ticks].
func _count_hours(ticks: Array[GameClock.TickData]) -> int:
	var count: int = 0
	for tick: GameClock.TickData in ticks:
		if tick is GameClock.OnHourPassed:
			count += 1
	return count


## Количество суточных тиков в [param ticks].
func _count_days(ticks: Array[GameClock.TickData]) -> int:
	var count: int = 0
	for tick: GameClock.TickData in ticks:
		if tick is GameClock.OnDayPassed:
			count += 1
	return count

#endregion
#endregion
