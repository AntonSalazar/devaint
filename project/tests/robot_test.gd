extends TestCase

## Тесты [Robot]: расход батареи по долям состояний, кламп,
## полнота таблиц, битовая маска состояний и излучение, ореол, отписка от шины.
## Изометрия движения покрыта в iso_test.gd.

#region CONSTANTS

## Сцена робота.
const ROBOT_SCENE: String = "res://core/robot/robot.tscn"

#endregion


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
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
	check_near(robot.get_battery(), Robot.BATTERY_MAX, "init fills the battery")
	robot.deinit()
	robot.free()


## Без накопленных секунд минутный тик не тратит заряд.
func test_no_observed_time_no_drain() -> void:
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
	_push_minute()
	check_near(robot.get_battery(), Robot.BATTERY_MAX, "no seconds - no drain")
	robot.deinit()
	robot.free()


## Минута чистого простоя стоит DRAIN[IDLE].
func test_idle_minute_drain() -> void:
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
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
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
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
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
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
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
	robot._seconds[Robot.State.IDLE] = 2.5
	_push_minute()
	var after_first: float = robot.get_battery()
	_push_minute()
	check_near(robot.get_battery(), after_first, "second tick without seconds is free")
	robot.deinit()
	robot.free()


## Заряд не уходит ниже нуля.
func test_battery_clamps_at_zero() -> void:
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
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


## Каждому состоянию задано излучение в EMISSION.
func test_emission_table_is_complete() -> void:
	for state: Robot.State in Robot.State.values():
		check_true(Robot.EMISSION.has(state), "EMISSION has an entry for state %d" % state)


## После init: маска IDLE, излучение IDLE, ореол построен.
func test_initial_state_and_halo() -> void:
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
	check_eq(robot.get_flags(), Robot.State.IDLE, "initial flags are IDLE")
	check_true(robot.has_flag(Robot.State.IDLE), "has_flag(IDLE)")
	check_true(not robot.has_flag(Robot.State.WALK), "not has_flag(WALK)")
	check_near(robot.get_emission(), Robot.EMISSION[Robot.State.IDLE], "idle emission")
	var halo: Halo = robot.get_node("%Halo") as Halo
	check_eq(halo.polygon.size(), 4, "halo quad is built right after init")
	robot.deinit()
	robot.free()


## Излучение суммируется по поднятым битам маски.
func test_emission_sums_over_flags() -> void:
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
	robot._flags = Robot.State.WALK
	check_near(robot.get_emission(), Robot.EMISSION[Robot.State.WALK], "walk emission")
	robot._flags = Robot.State.IDLE | Robot.State.WALK
	check_near(
			robot.get_emission(),
			Robot.EMISSION[Robot.State.IDLE] + Robot.EMISSION[Robot.State.WALK],
			"combined flags sum their emission"
	)
	robot.deinit()
	robot.free()


## Смена маски обновляет ореол; та же маска — нет.
func test_flags_drive_halo() -> void:
	var robot: Robot = _spawn_robot()
	robot.init(GameClock.new(), _make_grid())
	var halo: Halo = robot.get_node("%Halo") as Halo
	var shader: ShaderMaterial = halo.material as ShaderMaterial
	
	robot._flags = Robot.State.WALK
	halo._process(1.0)
	var walk_radius: float = shader.get_shader_parameter("radius")
	check_near(
			walk_radius,
			Halo.BASE_RADIUS + Halo.RADIUS_PER_EMISSION * Robot.EMISSION[Robot.State.WALK],
			"walk radius follows emission"
	)
	robot._flags = Robot.State.IDLE
	halo._process(1.0)
	check_true(
			shader.get_shader_parameter("radius") < walk_radius,
			"idle halo is smaller than walk halo"
	)
	robot.deinit()
	robot.free()


## Физический кадр без ввода: локомоция IDLE, чужие биты маски сохраняются,
## секунды копятся по IDLE.
func test_physics_frame_idle_keeps_modifier_bits() -> void:
	var robot: Robot = _spawn_robot()
	robot.init(GameClock.new(), _make_grid())
	var modifier: int = 0x10
	
	robot._flags = Robot.State.WALK | modifier
	robot._physics_process(0.1)
	check_true(robot.has_flag(Robot.State.IDLE), "no input -> IDLE locomotion")
	check_true(not robot.has_flag(Robot.State.WALK), "WALK bit is cleared")
	check_true(robot.get_flags() & modifier != 0, "modifier bit survives locomotion update")
	check_near(robot._seconds.get(Robot.State.IDLE, 0.0), 0.1, "idle seconds accumulated")
	check_true(not robot._seconds.has(Robot.State.WALK), "no walk seconds")
	robot.deinit()
	robot.free()


## На паузе физический кадр ничего не копит.
func test_physics_frame_on_pause_is_inert() -> void:
	var robot: Robot = _spawn_robot()
	var clock: GameClock = GameClock.new()
	robot.init(clock, _make_grid())
	
	clock.set_speed(0)
	robot._physics_process(0.1)
	check_true(robot._seconds.is_empty(), "paused frame accumulates nothing")
	robot.deinit()
	robot.free()


## Физический кадр вписывает след в память роя: излучение x покрытие x минуты.
func test_physics_frame_accumulates_notice() -> void:
	var robot: Robot = _spawn_robot()
	var grid: SignalGrid = _make_grid()
	robot.init(GameClock.new(), grid)
	robot.global_position = Iso.cell_to_world(Vector2i.ZERO)
	
	robot._physics_process(0.1)
	var expected: float = (
			Robot.EMISSION[Robot.State.IDLE] * SignalGrid.NOTICE_RATE
			* 0.1 / GameClock.MINUTE_DURATION
	)
	check_near(grid.get_notice(Vector2i.ZERO), expected, "one idle frame at full coverage")
	check_near(robot.get_notice(), expected, "robot reads its own sector notice")
	robot.deinit()
	robot.free()


## На паузе след не пишется.
func test_physics_frame_on_pause_accumulates_nothing() -> void:
	var robot: Robot = _spawn_robot()
	var grid: SignalGrid = _make_grid()
	var clock: GameClock = GameClock.new()
	robot.init(clock, grid)
	robot.global_position = Iso.cell_to_world(Vector2i.ZERO)
	
	clock.set_speed(0)
	robot._physics_process(0.1)
	check_true(grid.get_notices().is_empty(), "no notice while paused")
	robot.deinit()
	robot.free()


## После deinit подписок на шине не остается.
func test_deinit_unsubscribes() -> void:
	var robot: Robot = _spawn_robot()
	
	robot.init(GameClock.new(), _make_grid())
	check_true(not EventBus._ref.subs.is_empty(), "robot is subscribed after init")
	robot.deinit()
	var records_left: int = 0
	for records: Array in EventBus._ref.subs.values():
		records_left += records.size()
	check_eq(records_left, 0, "no subscription records after deinit")
	robot.free()

#endregion

#region REGULAR_PRIVATE

## Создание робота из сцены с добавлением в корень дерева (нужен %Halo).
func _spawn_robot() -> Robot:
	var scene: PackedScene = load(ROBOT_SCENE)
	var robot: Robot = scene.instantiate() as Robot
	(Engine.get_main_loop() as SceneTree).root.add_child(robot)
	return robot


## Сеть роя для тестов: вышка в (0,0), полное покрытие в ее клетке.
func _make_grid() -> SignalGrid:
	var grid: SignalGrid = SignalGrid.new(Vector2i(-10, -10), Vector2i(21, 21))
	grid.add_tower(Vector2i.ZERO, 300.0)
	return grid


## Публикация минутного тика с валидным снимком времени.
func _push_minute() -> void:
	GameClock.OnMinutePassed.new(1, {"minute": 1, "hour": 0, "day": 1}).push()

#endregion
#endregion
