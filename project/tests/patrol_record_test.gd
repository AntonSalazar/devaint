extends TestCase

## Тесты [PatrolRecord] и [PatrolPath]: период, стоянки, движение по отрезкам,
## замкнутость, пинг-понг, сдвиг расписания, отсутствие телепортов.

#region CONSTANTS

## Путевые клетки тестового маршрута.
const CELLS: Array[Vector2i] = [Vector2i(0, 0), Vector2i(4, 0), Vector2i(4, 4)]

## Скорость тестового маршрута, наземных px за игровую минуту.
const SPEED: float = 200.0

## Стоянка тестового маршрута, игровых минут.
const DWELL: float = 0.5

#endregion


#region FUNCTIONS
#region TESTS

## Период замкнутого маршрута: сумма стоянок и наземных длин по скорости.
func test_loop_period() -> void:
	var record: PatrolRecord = PatrolRecord.new(_route(true))
	check_eq(record.get_segment_count(), CELLS.size(), "loop: segment per cell")
	check_near(record.get_period(), _loop_period(), "period is dwells plus travels")


## В нулевой момент патруль стоит на первой клетке.
func test_sample_at_start() -> void:
	var record: PatrolRecord = PatrolRecord.new(_route(true))
	var sample: PatrolRecord.Sample = record.sample(0.0)
	check_eq(sample.position, Iso.cell_to_world(CELLS[0]), "position is the first cell")
	check_true(sample.dwelling, "dwelling at start")
	check_eq(sample.segment, 0, "segment 0")


## Середина первого перехода: lerp между центрами клеток, не стоянка.
func test_sample_mid_segment() -> void:
	var record: PatrolRecord = PatrolRecord.new(_route(true))
	var travel: float = _travel(CELLS[0], CELLS[1])
	var sample: PatrolRecord.Sample = record.sample(DWELL + travel / 2.0)
	var expected: Vector2 = Iso.cell_to_world(CELLS[0]).lerp(Iso.cell_to_world(CELLS[1]), 0.5)
	check_near(sample.position.x, expected.x, "midpoint x")
	check_near(sample.position.y, expected.y, "midpoint y")
	check_true(not sample.dwelling, "moving, not dwelling")


## Середина ВТОРОГО перехода: время прошлых отрезков списывается полностью.
func test_sample_second_segment() -> void:
	var record: PatrolRecord = PatrolRecord.new(_route(true))
	var t: float = DWELL + _travel(CELLS[0], CELLS[1]) + DWELL + _travel(CELLS[1], CELLS[2]) / 2.0
	var sample: PatrolRecord.Sample = record.sample(t)
	var expected: Vector2 = Iso.cell_to_world(CELLS[1]).lerp(Iso.cell_to_world(CELLS[2]), 0.5)
	check_eq(sample.segment, 1, "second segment")
	check_near(sample.position.x, expected.x, "midpoint x of segment 2")
	check_near(sample.position.y, expected.y, "midpoint y of segment 2")


## Через период маршрут возвращается в ту же точку.
func test_sample_wraps_by_period() -> void:
	var record: PatrolRecord = PatrolRecord.new(_route(true))
	for t: float in [0.1, 1.7, 3.3]:
		var a: PatrolRecord.Sample = record.sample(t)
		var b: PatrolRecord.Sample = record.sample(t + record.get_period())
		check_near(a.position.x, b.position.x, "x wraps at t=%.1f" % t)
		check_near(a.position.y, b.position.y, "y wraps at t=%.1f" % t)


## Сдвиг расписания смещает весь маршрут во времени.
func test_start_offset() -> void:
	var base: PatrolRecord = PatrolRecord.new(_route(true))
	var shifted_route: PatrolRoute = _route(true)
	shifted_route.start_offset_minutes = 1.25
	var shifted: PatrolRecord = PatrolRecord.new(shifted_route)
	for t: float in [0.0, 0.8, 2.1]:
		var a: PatrolRecord.Sample = base.sample(t)
		var b: PatrolRecord.Sample = shifted.sample(t + 1.25)
		check_near(a.position.x, b.position.x, "offset shifts x at t=%.1f" % t)
		check_near(a.position.y, b.position.y, "offset shifts y at t=%.1f" % t)


## Незамкнутый маршрут идет обратно по тем же точкам.
func test_ping_pong() -> void:
	var record: PatrolRecord = PatrolRecord.new(_route(false))
	check_eq(record.get_segment_count(), (CELLS.size() - 1) * 2, "segments there and back")
	# Середина последнего (обратного) отрезка: из CELLS[1] в CELLS[0].
	var t: float = record.get_period() - _travel(CELLS[1], CELLS[0]) / 2.0
	var sample: PatrolRecord.Sample = record.sample(t)
	var expected: Vector2 = Iso.cell_to_world(CELLS[1]).lerp(Iso.cell_to_world(CELLS[0]), 0.5)
	check_near(sample.position.x, expected.x, "return leg x")
	check_near(sample.position.y, expected.y, "return leg y")


## Патруль не телепортируется: наземный шаг не быстрее скорости.
func test_no_teleports() -> void:
	var record: PatrolRecord = PatrolRecord.new(_route(true))
	var step: float = 0.05
	var violations: int = 0
	var previous: Vector2 = record.sample(0.0).position
	var t: float = step
	while t < record.get_period() * 2.0:
		var current: Vector2 = record.sample(t).position
		if Iso.ground_distance(previous, current) > SPEED * step + 0.001:
			violations += 1
		previous = current
		t += step
	check_eq(violations, 0, "ground step never exceeds speed * dt")


## Маршрут из одной клетки — вечная стоянка в ее центре.
func test_single_cell_route() -> void:
	var route: PatrolRoute = PatrolRoute.new([Vector2i(2, 2)], SPEED, DWELL, true, 0.0)
	var record: PatrolRecord = PatrolRecord.new(route)
	check_true(record.get_period() > 0.0, "period is positive")
	for t: float in [0.0, 5.0, 123.4]:
		var sample: PatrolRecord.Sample = record.sample(t)
		check_eq(sample.position, Iso.cell_to_world(Vector2i(2, 2)), "stays at t=%.1f" % t)


## Пустой маршрут — ошибка без падения, безопасные ответы.
func test_empty_route_is_safe() -> void:
	print("        (an ERROR from PatrolRecord is expected below — part of the test)")
	var record: PatrolRecord = PatrolRecord.new(PatrolRoute.new([], SPEED, DWELL, true, 0.0))
	check_true(record.get_period() > 0.0, "period stays positive")
	check_true(record.sample(1.0).dwelling, "sample answers safely")


## PatrolPath собирает маршрут из кривой со схлопыванием дублей.
func test_patrol_path_builds_route() -> void:
	var path: PatrolPath = PatrolPath.new()
	path.curve = Curve2D.new()
	path.curve.add_point(Iso.cell_to_world(Vector2i(0, 0)))
	path.curve.add_point(Iso.cell_to_world(Vector2i(0, 0)) + Vector2(4.0, 0.0))
	path.curve.add_point(Iso.cell_to_world(Vector2i(3, 0)))
	path.speed = 123.0
	path.dwell_minutes = 0.25
	path.loop = false
	path.start_offset_minutes = 2.0
	
	var route: PatrolRoute = path.build_route()
	check_eq(route.cells, [Vector2i(0, 0), Vector2i(3, 0)] as Array[Vector2i], "cells deduped")
	check_near(route.speed, 123.0, "speed copied")
	check_near(route.dwell_minutes, 0.25, "dwell copied")
	check_true(not route.loop, "loop copied")
	check_near(route.start_offset_minutes, 2.0, "offset copied")
	path.free()

#endregion

#region REGULAR_PRIVATE

## Тестовый маршрут по CELLS.
func _route(loop: bool) -> PatrolRoute:
	return PatrolRoute.new(CELLS.duplicate(), SPEED, DWELL, loop, 0.0)


## Длительность перехода между клетками, игровых минут.
func _travel(from: Vector2i, to: Vector2i) -> float:
	return Iso.ground_distance(Iso.cell_to_world(from), Iso.cell_to_world(to)) / SPEED


## Период замкнутого маршрута по CELLS.
func _loop_period() -> float:
	var period: float = DWELL * CELLS.size()
	for idx: int in CELLS.size():
		period += _travel(CELLS[idx], CELLS[(idx + 1) % CELLS.size()])
	return period

#endregion
#endregion
