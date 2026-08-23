extends TestCase

## Тесты [Patrol]: позиция из расписания, веер конуса, взгляд по курсу,
## круговой обзор на стоянке, заморозка на паузе, деинициализация.

#region CONSTANTS

## Сцена патруля.
const PATROL_SCENE: String = "res://core/patrol/patrol.tscn"

## Путевые клетки тестового маршрута.
const CELLS: Array[Vector2i] = [Vector2i(0, 0), Vector2i(4, 0), Vector2i(4, 4)]

## Скорость, наземных px за игровую минуту.
const SPEED: float = 200.0

## Стоянка, игровых минут.
const DWELL: float = 0.5

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

## Веер конуса построен в _ready: центр + дуга.
func test_cone_fan_is_built() -> void:
	var patrol: Patrol = _spawn_patrol()
	var cone: Polygon2D = patrol.get_node("%Cone") as Polygon2D
	check_eq(cone.polygon.size(), 14, "fan has origin plus 13 arc points")
	if cone.polygon.size() == 14:
		check_eq(cone.polygon[0], Vector2.ZERO, "fan starts at the origin")
		check_near(
				cone.polygon[1].length(), Patrol.CONE_LENGTH,
				"arc points sit at CONE_LENGTH"
		)


## Позиция узла следует расписанию при движении времени.
func test_position_follows_schedule() -> void:
	var clock: GameClock = GameClock.new()
	var record: PatrolRecord = PatrolRecord.new(_route())
	var patrol: Patrol = _spawn_patrol()
	patrol.init(clock, record)
	
	patrol._process(0.0)
	check_eq(patrol.global_position, record.sample(0.0).position, "position at t = 0")
	
	var minutes: float = DWELL + 0.4
	clock.advance(minutes * GameClock.MINUTE_DURATION)
	patrol._process(0.0)
	var expected: Vector2 = record.sample(clock.get_time_minutes()).position
	check_near(patrol.global_position.x, expected.x, "x follows the schedule")
	check_near(patrol.global_position.y, expected.y, "y follows the schedule")
	patrol.deinit()


## На ходу конус смотрит по курсу в наземном угле.
func test_cone_faces_heading_while_moving() -> void:
	var clock: GameClock = GameClock.new()
	var record: PatrolRecord = PatrolRecord.new(_route())
	var patrol: Patrol = _spawn_patrol()
	patrol.init(clock, record)
	
	# Середина первого перехода.
	var travel: float = record.sample(DWELL + 0.01).heading.length()
	clock.advance((DWELL + _travel(CELLS[0], CELLS[1]) / 2.0) * GameClock.MINUTE_DURATION)
	patrol._process(0.0)
	var sample: PatrolRecord.Sample = record.sample(clock.get_time_minutes())
	check_true(not sample.dwelling, "patrol is moving")
	var heading: Vector2 = sample.heading
	var expected: float = Vector2(heading.x, heading.y / Iso.SCALE_Y).angle()
	var pivot: Node2D = patrol.get_node("%ConePivot") as Node2D
	check_near(pivot.rotation, expected, "cone pivot faces the ground angle of heading")
	check_true(is_finite(travel), "sanity")
	patrol.deinit()


## На стоянке конус водит по кругу со временем игры.
func test_cone_scans_while_dwelling() -> void:
	var clock: GameClock = GameClock.new()
	var record: PatrolRecord = PatrolRecord.new(_route())
	var patrol: Patrol = _spawn_patrol()
	patrol.init(clock, record)
	var pivot: Node2D = patrol.get_node("%ConePivot") as Node2D
	
	patrol._process(0.0)
	var start_rotation: float = pivot.rotation
	clock.advance(0.2 * GameClock.MINUTE_DURATION)
	patrol._process(0.0)
	check_true(record.sample(clock.get_time_minutes()).dwelling, "still dwelling")
	check_near(
			pivot.rotation, fposmod(clock.get_time_minutes() * Patrol.SCAN_RATE, TAU),
			"scan angle is game-time driven"
	)
	check_true(absf(pivot.rotation - start_rotation) > 0.01, "the cone actually turns")
	patrol.deinit()


## Пауза замораживает и позицию, и обзор.
func test_pause_freezes_patrol() -> void:
	var clock: GameClock = GameClock.new()
	var record: PatrolRecord = PatrolRecord.new(_route())
	var patrol: Patrol = _spawn_patrol()
	patrol.init(clock, record)
	var pivot: Node2D = patrol.get_node("%ConePivot") as Node2D
	
	clock.advance((DWELL + 0.3) * GameClock.MINUTE_DURATION)
	patrol._process(0.0)
	var position: Vector2 = patrol.global_position
	var rotation: float = pivot.rotation
	
	clock.set_speed(0)
	clock.advance(1000.0)
	patrol._process(0.0)
	check_eq(patrol.global_position, position, "position is frozen on pause")
	check_near(pivot.rotation, rotation, "cone is frozen on pause")
	patrol.deinit()


## deinit глушит процессинг и отпускает ссылки.
func test_deinit_stops_processing() -> void:
	var patrol: Patrol = _spawn_patrol()
	patrol.init(GameClock.new(), PatrolRecord.new(_route()))
	check_true(patrol.is_processing(), "processing after init")
	patrol.deinit()
	check_true(not patrol.is_processing(), "processing stopped after deinit")

#endregion

#region REGULAR_PRIVATE

## Тестовый замкнутый маршрут по CELLS.
func _route() -> PatrolRoute:
	return PatrolRoute.new(CELLS.duplicate(), SPEED, DWELL, true, 0.0)


## Длительность перехода между клетками, игровых минут.
func _travel(from: Vector2i, to: Vector2i) -> float:
	return Iso.ground_distance(Iso.cell_to_world(from), Iso.cell_to_world(to)) / SPEED


## Создание патруля из сцены с добавлением в корень дерева.
func _spawn_patrol() -> Patrol:
	var patrol: Patrol = (load(PATROL_SCENE) as PackedScene).instantiate() as Patrol
	(Engine.get_main_loop() as SceneTree).root.add_child(patrol)
	_nodes.append(patrol)
	return patrol

#endregion
#endregion
