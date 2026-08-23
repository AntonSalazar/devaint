extends SceneTree

## Headless-прогон кривых заметности. Запуск: `just balance`.
##
## Гоняет настоящие [GameClock] и [SignalGrid] без рендера по сценариям
## (излучение x покрытие, спад) и печатает таблицы по минутам, чтобы
## подбирать NOTICE_RATE / DECAY по числам, а не по ощущениям.

#region CONSTANTS

## Горизонт сценария накопления, игровых минут.
const HORIZON_MINUTES: int = 60

## Шаг печати строк таблицы, минут.
const PRINT_EVERY: int = 5

## Пороги реакций сети (07-SIGNATURE-MATH.md).
const THRESHOLDS: Array[float] = [25.0, 50.0, 75.0, 100.0]

## Излучение зарядки от сети (07-SIGNATURE-MATH.md; в коде робота еще нет).
const CHARGE_EMISSION: float = 15.0

## Угол и размер тестового грида.
const ORIGIN: Vector2i = Vector2i(-12, -12)
const SIZE: Vector2i = Vector2i(25, 25)

#endregion


#region FUNCTIONS
#region OVERRIDE_PRIVATE

func _initialize() -> void:
	EventBus.reset()
	_run()
	EventBus.reset()
	quit(0)

#endregion

#region REGULAR_PRIVATE

## Функция прогона всех сценариев.
func _run() -> void:
	print("=== DevAInt notice balance ===")
	print("NOTICE_RATE = %.2f  DECAY_PER_MINUTE = %.2f  ABSENT_MULT = %.1f  TOWER_RADIUS = %.0f" % [
			SignalGrid.NOTICE_RATE, SignalGrid.DECAY_PER_MINUTE,
			SignalGrid.ABSENT_DECAY_MULTIPLIER, Main.TOWER_RADIUS,
	])
	print("emission: idle %.1f  walk %.1f  charge %.1f" % [
			Robot.EMISSION[Robot.State.IDLE], Robot.EMISSION[Robot.State.WALK], CHARGE_EMISSION,
	])
	
	# Позиции ищем по покрытию, а не задаем руками.
	var probe: SignalGrid = _make_grid()
	var spots: Dictionary[String, Vector2i] = {
		"1.0": _find_cell_with_coverage(probe, 1.0),
		"0.5": _find_cell_with_coverage(probe, 0.5),
		"0.2": _find_cell_with_coverage(probe, 0.2),
	}
	print("spots: full %s  half %s  edge %s" % [spots["1.0"], spots["0.5"], spots["0.2"]])
	
	# Сценарии накопления: [метка, излучение, ключ покрытия].
	var scenarios: Array[Array] = [
		["walk   @ 1.0", Robot.EMISSION[Robot.State.WALK], "1.0"],
		["idle   @ 1.0", Robot.EMISSION[Robot.State.IDLE], "1.0"],
		["walk   @ 0.5", Robot.EMISSION[Robot.State.WALK], "0.5"],
		["walk   @ 0.2", Robot.EMISSION[Robot.State.WALK], "0.2"],
		["charge @ 1.0", CHARGE_EMISSION, "1.0"],
		["charge @ 0.5", CHARGE_EMISSION, "0.5"],
	]
	for scenario: Array in scenarios:
		_run_accumulation(scenario[0], scenario[1], spots[scenario[2]])
	
	# Сценарии спада.
	_run_decay("decay, player stays (active sector)", true)
	_run_decay("decay, player left (absent sector)", false)


## Функция сценария накопления: [param label], излучение [param emission]
## в клетке [param cell] на протяжении HORIZON_MINUTES.
func _run_accumulation(label: String, emission: float, cell: Vector2i) -> void:
	var grid: SignalGrid = _make_grid()
	var clock: GameClock = GameClock.new()
	var position: Vector2 = Iso.cell_to_world(cell)
	grid.init()
	print("\n--- %s  (coverage %.2f, emission %.1f) ---" % [
			label, grid.get_coverage(cell), emission,
	])
	
	var reached: Dictionary[float, int] = {}
	var row: String = ""
	for minute: int in range(1, HORIZON_MINUTES + 1):
		# Пик минуты — после накопления, до спада (пороги в игре ловятся так же).
		grid.accumulate(position, emission, 1.0)
		var value: float = grid.get_notice_at(position)
		clock.advance(GameClock.MINUTE_DURATION)
		for threshold: float in THRESHOLDS:
			if value >= threshold and not reached.has(threshold):
				reached[threshold] = minute
		if minute % PRINT_EVERY == 0:
			row += "  %2d:%5.1f" % [minute, value]
		if value >= THRESHOLDS[-1]:
			break
	print(row)
	print("  " + _summary(reached))
	grid.deinit()


## Функция сценария спада с 100% до нуля: [param active] — игрок остался в секторе.
func _run_decay(label: String, active: bool) -> void:
	var grid: SignalGrid = _make_grid()
	var clock: GameClock = GameClock.new()
	var home: Vector2 = Iso.cell_to_world(Vector2i.ZERO)
	var away: Vector2 = Iso.cell_to_world(Vector2i(SignalGrid.SECTOR_SIZE + 1, 0))
	grid.init()
	grid.accumulate(home, CHARGE_EMISSION, 1000.0)
	if not active:
		grid.accumulate(away, 0.0, 1.0)
	
	var minutes: int = 0
	while grid.get_notice(Vector2i.ZERO) > 0.0 and minutes < 1000:
		clock.advance(GameClock.MINUTE_DURATION)
		minutes += 1
	print("\n--- %s ---\n  100%% -> 0%% in %d min" % [label, minutes])
	grid.deinit()


## Функция сводки по порогам [param reached]: минута достижения или never.
func _summary(reached: Dictionary[float, int]) -> String:
	var parts: Array[String] = []
	for threshold: float in THRESHOLDS:
		var when: String = "never"
		if reached.has(threshold):
			when = "%d min" % reached[threshold]
		parts.append("%d%% at %s" % [int(threshold), when])
	return ", ".join(parts)


## Функция поиска клетки с покрытием, ближайшим к [param target].
func _find_cell_with_coverage(grid: SignalGrid, target: float) -> Vector2i:
	var best: Vector2i = Vector2i.ZERO
	var best_delta: float = INF
	for x: int in range(0, grid.get_size().x):
		var cell: Vector2i = Vector2i(x, 0)
		var delta: float = absf(grid.get_coverage(cell) - target)
		if delta < best_delta:
			best_delta = delta
			best = cell
	return best


## Функция создания грида с одной вышкой в (0,0) и игровым радиусом.
func _make_grid() -> SignalGrid:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	grid.add_tower(Vector2i.ZERO, Main.TOWER_RADIUS)
	return grid

#endregion
#endregion
