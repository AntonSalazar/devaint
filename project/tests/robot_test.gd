extends TestCase

## Тесты [Robot]: расход батареи по долям состояний, кламп,
## полнота таблицы расхода, отписка от шины.
## Изометрия движения покрыта в iso_test.gd.

#region FUNCTIONS
#region OVERRIDE_PUBLIC

## Полный сброс шины перед каждым тестом.
func before_each() -> void:
	EventBus.reset()


## Сброс шины после теста.
func after_each() -> void:
	EventBus.reset()

#endregion

#region TESTS

## Инициализация заряжает батарею до максимума.
func test_init_fills_battery() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	check_near(robot.get_battery(), Robot.BATTERY_MAX, "init fills the battery")
	robot.deinit()
	robot.free()


## Без накопленных секунд минутный тик не тратит заряд.
func test_no_observed_time_no_drain() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	_push_minute()
	check_near(robot.get_battery(), Robot.BATTERY_MAX, "no seconds - no drain")
	robot.deinit()
	robot.free()


## Минута чистого простоя стоит DRAIN[IDLE].
func test_idle_minute_drain() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	robot._seconds[Robot.State.IDLE] = 2.5
	_push_minute()
	check_near(
			robot.get_battery(), Robot.BATTERY_MAX - Robot.DRAIN[Robot.State.IDLE],
			"idle minute costs DRAIN[IDLE]"
	)
	robot.deinit()
	robot.free()


## Минута чистой ходьбы стоит DRAIN[WALK].
func test_walk_minute_drain() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	robot._seconds[Robot.State.WALK] = 2.5
	_push_minute()
	check_near(
			robot.get_battery(), Robot.BATTERY_MAX - Robot.DRAIN[Robot.State.WALK],
			"walk minute costs DRAIN[WALK]"
	)
	robot.deinit()
	robot.free()


## Смешанная минута тратит взвешенное среднее по долям состояний.
func test_mixed_minute_drain() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	robot._seconds[Robot.State.IDLE] = 1.25
	robot._seconds[Robot.State.WALK] = 1.25
	_push_minute()
	var expected: float = (
			Robot.BATTERY_MAX
			- Robot.DRAIN[Robot.State.IDLE] * 0.5
			- Robot.DRAIN[Robot.State.WALK] * 0.5
	)
	check_near(robot.get_battery(), expected, "mixed minute drains a weighted average")
	robot.deinit()
	robot.free()


## Счетчики секунд очищаются после тика.
func test_seconds_reset_after_tick() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	robot._seconds[Robot.State.IDLE] = 2.5
	_push_minute()
	var after_first: float = robot.get_battery()
	_push_minute()
	check_near(robot.get_battery(), after_first, "second tick without seconds is free")
	robot.deinit()
	robot.free()


## Заряд не уходит ниже нуля.
func test_battery_clamps_at_zero() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	robot._battery = Robot.DRAIN[Robot.State.WALK] / 2.0
	robot._seconds[Robot.State.WALK] = 2.5
	_push_minute()
	check_near(robot.get_battery(), 0.0, "battery clamps at zero")
	robot.deinit()
	robot.free()


## Каждому состоянию задан расход в DRAIN.
func test_drain_table_is_complete() -> void:
	for state: Robot.State in Robot.State.values():
		check_true(
				Robot.DRAIN.has(state),
				"DRAIN has an entry for state %d" % state
		)


## После deinit подписок на шине не остается.
func test_deinit_unsubscribes() -> void:
	var robot: Robot = Robot.new()
	
	robot.init(GameClock.new())
	check_true(not EventBus._ref.subs.is_empty(), "robot is subscribed after init")
	robot.deinit()
	var records_left: int = 0
	for records: Array in EventBus._ref.subs.values():
		records_left += records.size()
	check_eq(records_left, 0, "no subscription records after deinit")
	robot.free()

#endregion

#region REGULAR_PRIVATE

## Публикация минутного тика с валидным снимком времени.
func _push_minute() -> void:
	GameClock.OnMinutePassed.new(1, {"minute": 1, "hour": 0, "day": 1}).push()

#endregion
#endregion
