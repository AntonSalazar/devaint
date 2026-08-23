extends TestCase

## Тесты [DebugOverlay]: шкала заметности (значение, уровень, цвет),
## экранный лог (строки, фильтр шума, длина буфера, очистка).

#region CONSTANTS

## Сцены.
const OVERLAY_SCENE: String = "res://core/debug_overlay/debug_overlay.tscn"
const ROBOT_SCENE: String = "res://core/robot/robot.tscn"

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Узлы текущего теста — освобождаются в after_each.
var _nodes: Array[Node] = []

#endregion
#endregion


#region FUNCTIONS
#region OVERRIDE_PUBLIC

## Сброс шины перед тестом.
func before_each() -> void:
	EventBus.reset()


## Освобождение узлов и сброс шины после теста.
func after_each() -> void:
	for node: Node in _nodes:
		node.free()
	_nodes.clear()
	EventBus.reset()

#endregion

#region TESTS

## Шкала показывает заметность робота, уровень и цвет уровня.
func test_notice_bar_follows_robot() -> void:
	var grid: SignalGrid = SignalGrid.new(Vector2i(-10, -10), Vector2i(21, 21))
	grid.add_tower(Vector2i.ZERO, 300.0)
	var robot: Robot = _spawn(ROBOT_SCENE) as Robot
	robot.init(GameClock.new(), grid)
	robot.global_position = Iso.cell_to_world(Vector2i.ZERO)
	var overlay: DebugOverlay = _spawn(OVERLAY_SCENE) as DebugOverlay
	overlay.init(GameClock.new(), robot)
	
	grid.accumulate(robot.global_position, 25.0 / SignalGrid.NOTICE_RATE, 1.0)
	overlay._process(0.0)
	var bar: ProgressBar = overlay.get_node("%NoticeBar") as ProgressBar
	var label: Label = overlay.get_node("%NoticeLevel") as Label
	check_near(bar.value, 25.0, "bar value equals robot notice")
	check_true("CURIOUS" in label.text, "label names the level")
	check_eq(
			bar.modulate, DebugOverlay.NOTICE_LEVEL_COLOR[SignalGrid.Level.CURIOUS],
			"bar is tinted by the level color"
	)
	overlay.deinit()
	robot.deinit()


## Лог: сообщение попадает на экран с игровым таймстампом, шум — нет.
func test_log_lines_and_filter() -> void:
	var overlay: DebugOverlay = _spawn(OVERLAY_SCENE) as DebugOverlay
	var robot: Robot = _spawn(ROBOT_SCENE) as Robot
	overlay.init(GameClock.new(), robot)
	var log_output: RichTextLabel = overlay.get_node("%Log") as RichTextLabel
	
	GameClock.OnHourPassed.new(60, {"minute": 0, "hour": 1, "day": 1}).push()
	check_true("GameClock.OnHourPassed" in log_output.text, "hour tick is logged")
	check_true(log_output.text.begins_with("[Day 1 "), "line starts with a game timestamp")
	var lines_before: int = log_output.text.split("\n").size()
	GameClock.OnMinutePassed.new(61, {"minute": 1, "hour": 1, "day": 1}).push()
	check_eq(log_output.text.split("\n").size(), lines_before, "minute tick is filtered out")
	overlay.deinit()


## Лог держит не больше LOG_LINES строк, старые выпадают с начала.
func test_log_ring_buffer() -> void:
	var overlay: DebugOverlay = _spawn(OVERLAY_SCENE) as DebugOverlay
	var robot: Robot = _spawn(ROBOT_SCENE) as Robot
	overlay.init(GameClock.new(), robot)
	var log_output: RichTextLabel = overlay.get_node("%Log") as RichTextLabel
	
	for idx: int in range(1, DebugOverlay.LOG_LINES + 3):
		SignalGrid.OnNoticeLevelChanged.new(
				Vector2i(idx, 0), SignalGrid.Level.NONE, SignalGrid.Level.CURIOUS, float(idx)
		).push()
	var lines: PackedStringArray = log_output.text.split("\n")
	check_eq(lines.size(), DebugOverlay.LOG_LINES, "buffer is capped at LOG_LINES")
	check_true("(3, 0)" in lines[0], "oldest kept line is the third published")
	check_true("(%d, 0)" % (DebugOverlay.LOG_LINES + 2) in lines[-1], "newest line is last")
	overlay.deinit()


## deinit очищает лог.
func test_deinit_clears_log() -> void:
	var overlay: DebugOverlay = _spawn(OVERLAY_SCENE) as DebugOverlay
	var robot: Robot = _spawn(ROBOT_SCENE) as Robot
	overlay.init(GameClock.new(), robot)
	var log_output: RichTextLabel = overlay.get_node("%Log") as RichTextLabel
	
	GameClock.OnHourPassed.new(60, {"minute": 0, "hour": 1, "day": 1}).push()
	overlay.deinit()
	check_true(overlay._log.is_empty(), "log buffer is cleared on deinit")
	check_eq(log_output.text, "", "log output is cleared on deinit")

#endregion

#region REGULAR_PRIVATE

## Создание узла из сцены с добавлением в корень дерева.
func _spawn(path: String) -> Node:
	var node: Node = (load(path) as PackedScene).instantiate()
	(Engine.get_main_loop() as SceneTree).root.add_child(node)
	_nodes.append(node)
	return node

#endregion
#endregion
