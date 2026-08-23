extends TestCase

## Тесты [SignalGrid]: пустой грид, поле одной вышки, нахлест и кламп,
## включение/выключение, границы грида, согласованность с Iso, картинка,
## событие перестройки; память секторов: накопление, кламп, спад, подписка.

#region CONSTANTS

## Угол тестового грида.
const ORIGIN: Vector2i = Vector2i(-10, -10)

## Размер тестового грида.
const SIZE: Vector2i = Vector2i(21, 21)

## Радиус тестовой вышки в мировых пикселях.
const RADIUS: float = 300.0

## Расстояние по земле между центрами соседних по оси клеток:
## (64, 32) на экране -> (64, 64) по земле.
const NEIGHBOR_DISTANCE: float = 90.50966799187809

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

## Пустой грид — нули везде, включая точки вне грида.
func test_empty_grid_is_zero() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	check_near(grid.get_coverage(Vector2i.ZERO), 0.0, "origin cell is zero")
	check_near(grid.get_coverage(ORIGIN), 0.0, "corner cell is zero")
	check_near(grid.get_coverage(Vector2i(100, 100)), 0.0, "outside cell is zero")
	check_eq(grid.get_size(), SIZE, "size is stored")
	check_eq(grid.get_origin(), ORIGIN, "origin is stored")


## Одна вышка: 1.0 в центре, линейный спад, 0.0 за радиусом.
func test_single_tower_field() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	
	check_near(grid.get_coverage(Vector2i.ZERO), 1.0, "tower cell is fully covered")
	check_near(
			grid.get_coverage(Vector2i(1, 0)), 1.0 - NEIGHBOR_DISTANCE / RADIUS,
			"neighbor cell follows the linear falloff"
	)
	check_true(
			grid.get_coverage(Vector2i(1, 0)) > grid.get_coverage(Vector2i(2, 0))
			and grid.get_coverage(Vector2i(2, 0)) > grid.get_coverage(Vector2i(3, 0)),
			"coverage decreases with distance"
	)
	check_near(grid.get_coverage(Vector2i(5, 0)), 0.0, "cell beyond the radius is zero")


## Поле симметрично по земле: клетки на равном наземном расстоянии равны.
func test_field_is_ground_symmetric() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	
	var diagonal: float = grid.get_coverage(Vector2i(1, 1))
	check_near(grid.get_coverage(Vector2i(1, -1)), diagonal, "(1,-1) equals (1,1)")
	check_near(grid.get_coverage(Vector2i(-1, -1)), diagonal, "(-1,-1) equals (1,1)")
	check_near(grid.get_coverage(Vector2i(-1, 1)), diagonal, "(-1,1) equals (1,1)")


## Две вышки внахлест — сумма клампится в 1.0.
func test_overlap_clamps_to_one() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	grid.add_tower(Vector2i(1, 0), RADIUS)
	
	check_near(grid.get_coverage(Vector2i.ZERO), 1.0, "overlap does not exceed 1.0")
	check_near(grid.get_coverage(Vector2i(1, 0)), 1.0, "second tower cell is also 1.0")


## Выключение вышки обнуляет поле, включение — возвращает.
func test_tower_toggle() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	var id: int = grid.add_tower(Vector2i.ZERO, RADIUS)
	
	grid.set_tower_active(id, false)
	check_near(grid.get_coverage(Vector2i.ZERO), 0.0, "disabled tower gives no coverage")
	grid.set_tower_active(id, true)
	check_near(grid.get_coverage(Vector2i.ZERO), 1.0, "re-enabled tower restores coverage")


## Неизвестный id вышки — ошибка без падения и без изменений поля.
func test_unknown_tower_id_is_safe() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	
	print("        (an ERROR from SignalGrid is expected below — part of the test)")
	grid.set_tower_active(99, false)
	check_near(grid.get_coverage(Vector2i.ZERO), 1.0, "field is unchanged after a bad id")


## Вышка за пределами грида не ломает его, поле обрезается границей.
func test_tower_outside_grid() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i(12, 0), RADIUS)
	
	check_near(grid.get_coverage(Vector2i(12, 0)), 0.0, "outside cell reads zero")
	check_true(grid.get_coverage(Vector2i(10, 0)) > 0.0, "edge cell inside the grid is covered")


## Запрос по мировой точке согласован с запросом по клетке.
func test_coverage_at_matches_cell() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	
	var mismatches: int = 0
	for x: int in range(-3, 4):
		for y: int in range(-3, 4):
			var cell: Vector2i = Vector2i(x, y)
			var by_point: float = grid.get_coverage_at(Iso.cell_to_world(cell))
			if not is_equal_approx(by_point, grid.get_coverage(cell)):
				mismatches += 1
	check_eq(mismatches, 0, "get_coverage_at agrees with get_coverage")


## Картинка статики: размер грида, R-канал = покрытие клетки.
func test_static_image() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	var image: Image = grid.get_static_image()
	
	check_eq(image.get_size(), SIZE, "image size equals grid size")
	check_eq(image.get_format(), Image.FORMAT_RF, "image format is RF")
	var local: Vector2i = Vector2i(1, 0) - ORIGIN
	check_near(
			image.get_pixel(local.x, local.y).r, grid.get_coverage(Vector2i(1, 0)),
			"pixel R equals cell coverage"
	)


## OnStaticChanged публикуется на каждой перестройке.
func test_static_changed_message() -> void:
	var count: Array[int] = []
	EventBus.subscribe(
			SignalGrid.OnStaticChanged,
			func(_message: SignalGrid.OnStaticChanged) -> void: count.append(1)
	)
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	
	var id: int = grid.add_tower(Vector2i.ZERO, RADIUS)
	check_eq(count.size(), 1, "add_tower publishes OnStaticChanged")
	grid.set_tower_active(id, false)
	check_eq(count.size(), 2, "toggle publishes OnStaticChanged")
	grid.set_tower_active(id, false)
	check_eq(count.size(), 2, "no-op toggle does not publish")


## Сектор клетки: квадраты SECTOR_SIZE, отрицательные клетки не схлопываются.
func test_cell_to_sector() -> void:
	var size: int = SignalGrid.SECTOR_SIZE
	check_eq(SignalGrid.cell_to_sector(Vector2i.ZERO), Vector2i.ZERO, "(0,0) -> (0,0)")
	check_eq(
			SignalGrid.cell_to_sector(Vector2i(size - 1, size - 1)), Vector2i.ZERO,
			"last cell of sector 0"
	)
	check_eq(SignalGrid.cell_to_sector(Vector2i(size, 0)), Vector2i(1, 0), "first cell of sector 1")
	check_eq(SignalGrid.cell_to_sector(Vector2i(-1, -1)), Vector2i(-1, -1), "(-1,-1) -> (-1,-1)")
	check_eq(
			SignalGrid.cell_to_sector(Vector2i(-size, 3)), Vector2i(-1, 0), "(-size,3) -> (-1,0)"
	)
	check_eq(
			SignalGrid.cell_to_sector(Vector2i(-size - 1, 0)), Vector2i(-2, 0),
			"(-size-1,0) -> (-2,0)"
	)


## Накопление: излучение x покрытие x NOTICE_RATE x минуты в сектор позиции.
func test_accumulate_full_coverage() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	var center: Vector2 = Iso.cell_to_world(Vector2i.ZERO)
	
	grid.accumulate(center, 3.0, 1.0)
	check_near(
			grid.get_notice(Vector2i.ZERO), 3.0 * SignalGrid.NOTICE_RATE,
			"walk emission for one minute at full coverage"
	)
	check_near(grid.get_notice_at(center), grid.get_notice(Vector2i.ZERO), "get_notice_at agrees")
	grid.accumulate(center, 1.0, 0.5)
	check_near(
			grid.get_notice(Vector2i.ZERO), 3.5 * SignalGrid.NOTICE_RATE,
			"accumulation adds up"
	)


## Без покрытия след не пишется и запись сектора не создается.
func test_accumulate_without_coverage() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	
	grid.accumulate(Iso.cell_to_world(Vector2i(9, 9)), 25.0, 10.0)
	check_near(grid.get_notice(SignalGrid.cell_to_sector(Vector2i(9, 9))), 0.0, "no notice")
	check_true(grid.get_notices().is_empty(), "no sector record is created for zero gain")


## Заметность клампится в NOTICE_MAX.
func test_accumulate_clamps() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), 25.0, 1000.0)
	check_near(grid.get_notice(Vector2i.ZERO), SignalGrid.NOTICE_MAX, "clamped at NOTICE_MAX")


## След пишется в сектор позиции, а не куда-то еще.
func test_accumulate_targets_own_sector() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	var far: Vector2i = Vector2i(SignalGrid.SECTOR_SIZE + 1, 1)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	grid.add_tower(far, RADIUS)
	
	grid.accumulate(Iso.cell_to_world(far), 1.0, 1.0)
	check_near(grid.get_notice(Vector2i(1, 0)), SignalGrid.NOTICE_RATE, "far sector got the notice")
	check_near(grid.get_notice(Vector2i.ZERO), 0.0, "home sector is untouched")


## Спад по минутному тику: активный сектор медленнее, чужие быстрее, нули удаляются.
func test_decay_on_minute_tick() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	var far: Vector2i = Vector2i(SignalGrid.SECTOR_SIZE + 1, 1)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	grid.add_tower(far, RADIUS)
	grid.init()
	
	var start: float = 10.0 / SignalGrid.NOTICE_RATE
	grid.accumulate(Iso.cell_to_world(far), start, 1.0)
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), start, 1.0)
	_push_minute()
	check_near(
			grid.get_notice(Vector2i.ZERO), 10.0 - SignalGrid.DECAY_PER_MINUTE,
			"active sector decays by DECAY_PER_MINUTE"
	)
	check_near(
			grid.get_notice(Vector2i(1, 0)),
			10.0 - SignalGrid.DECAY_PER_MINUTE * SignalGrid.ABSENT_DECAY_MULTIPLIER,
			"absent sector decays faster"
	)
	
	grid.accumulate(Iso.cell_to_world(far), 1.0, 1.0)
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), 0.0, 1.0)
	for _idx: int in 10:
		_push_minute()
	check_true(
			not grid.get_notices().has(Vector2i(1, 0)),
			"sector that decayed to zero is erased"
	)
	grid.deinit()


## Снимок секторов — копия: правка снимка не трогает грид.
func test_notices_snapshot_is_a_copy() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), 1.0, 1.0)
	
	var snapshot: Dictionary[Vector2i, float] = grid.get_notices()
	snapshot[Vector2i.ZERO] = 99.0
	check_near(
			grid.get_notice(Vector2i.ZERO), SignalGrid.NOTICE_RATE,
			"grid is unaffected by snapshot edits"
	)


## init подписывает грид на тики, deinit — отписывает.
func test_init_deinit_subscription() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	
	grid.init()
	check_true(not EventBus._ref.subs.is_empty(), "subscribed after init")
	grid.deinit()
	var records_left: int = 0
	for records: Array in EventBus._ref.subs.values():
		records_left += records.size()
	check_eq(records_left, 0, "no subscription records after deinit")



## Подъем через 25% публикует NONE -> CURIOUS с сектором и значением.
func test_level_up_publishes() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	var messages: Array[SignalGrid.OnNoticeLevelChanged] = _collect_levels()
	
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), 25.0 / SignalGrid.NOTICE_RATE, 1.0)
	check_eq(messages.size(), 1, "one level message")
	if messages.size() == 1:
		check_eq(messages[0].sector, Vector2i.ZERO, "sector in payload")
		check_eq(messages[0].previous, SignalGrid.Level.NONE, "previous NONE")
		check_eq(messages[0].level, SignalGrid.Level.CURIOUS, "level CURIOUS")
		check_near(messages[0].value, 25.0, "value in payload")
	check_eq(grid.get_level(Vector2i.ZERO), SignalGrid.Level.CURIOUS, "get_level")
	check_eq(
			grid.get_level_at(Iso.cell_to_world(Vector2i.ZERO)), SignalGrid.Level.CURIOUS,
			"get_level_at agrees"
	)


## Скачок через несколько порогов разом — одно сообщение до верхнего уровня.
func test_level_jump_publishes_once() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	var messages: Array[SignalGrid.OnNoticeLevelChanged] = _collect_levels()
	
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), 60.0 / SignalGrid.NOTICE_RATE, 1.0)
	check_eq(messages.size(), 1, "a single message for a multi-threshold jump")
	if messages.size() == 1:
		check_eq(messages[0].previous, SignalGrid.Level.NONE, "from NONE")
		check_eq(messages[0].level, SignalGrid.Level.SCOUT, "straight to SCOUT")


## Гистерезис: уровень держится до порога минус LEVEL_HYSTERESIS.
func test_level_hysteresis_on_decay() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	var messages: Array[SignalGrid.OnNoticeLevelChanged] = _collect_levels()
	grid.init()
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), 25.0 / SignalGrid.NOTICE_RATE, 1.0)
	check_eq(messages.size(), 1, "CURIOUS reached")
	
	# Один тик: 25 -> 23.5, это выше 25 - 5 — уровень держится, сообщений нет.
	_push_minute()
	check_eq(grid.get_level(Vector2i.ZERO), SignalGrid.Level.CURIOUS, "still CURIOUS at 23.5")
	check_eq(messages.size(), 1, "no message inside the hysteresis band")
	
	# Тикаем, пока не упадем ниже 20: должно прийти CURIOUS -> NONE.
	for _idx: int in 3:
		_push_minute()
	check_eq(grid.get_level(Vector2i.ZERO), SignalGrid.Level.NONE, "dropped to NONE below 20")
	check_eq(messages.size(), 2, "one message for the drop")
	if messages.size() == 2:
		check_eq(messages[1].previous, SignalGrid.Level.CURIOUS, "drop: previous CURIOUS")
		check_eq(messages[1].level, SignalGrid.Level.NONE, "drop: level NONE")
	grid.deinit()


## Сектор, остывший до нуля, снимает уровень сообщением.
func test_level_cleared_on_erase() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, RADIUS)
	var messages: Array[SignalGrid.OnNoticeLevelChanged] = _collect_levels()
	grid.init()
	grid.accumulate(Iso.cell_to_world(Vector2i.ZERO), 26.0 / SignalGrid.NOTICE_RATE, 1.0)
	
	for _idx: int in 30:
		_push_minute()
	check_true(grid.get_notices().is_empty(), "record erased")
	check_eq(grid.get_level(Vector2i.ZERO), SignalGrid.Level.NONE, "level NONE after erase")
	check_eq(messages[-1].level, SignalGrid.Level.NONE, "last message is the drop to NONE")
	grid.deinit()

#endregion

#region REGULAR_PRIVATE

## Подписка-сборщик сообщений о смене уровня.
func _collect_levels() -> Array[SignalGrid.OnNoticeLevelChanged]:
	var messages: Array[SignalGrid.OnNoticeLevelChanged] = []
	var collector: Callable = func(message: SignalGrid.OnNoticeLevelChanged) -> void:
		messages.append(message)
	EventBus.subscribe(SignalGrid.OnNoticeLevelChanged, collector)
	return messages


## Публикация минутного тика с валидным снимком времени.
func _push_minute() -> void:
	GameClock.OnMinutePassed.new(1, {"minute": 1, "hour": 0, "day": 1}).push()

#endregion
#endregion
