class_name PatrolRecord
extends RefCounted

## Запись патруля.
##
## Берет маршрут [PatrolRoute], предвычисляет отрезки и отвечает на вопрос
## "где патруль в момент T". Не тикает, и не знает о сцене - это тёплый агент
## из 09-ARCHITECTURE.md. Активная сцена просто ставит себя в
## [code]sample(clock.get_time_minutes()).position[/code].
## Длины отрезков и скорость - в наземных px [method Iso.ground_distance],
## чтобы темп патруля был сравним с роботом [Robot] не зависимо от направления.


#region VARIABLES
#region REGULAR_PRIVATE

## Ссылка на экземпляр маршрута.
var _route: PatrolRoute = null

## Предвычисленные отрезки в порядке обхода.
var _segments: Array[Segment] = []

## Период расписания в игровых минутах.
var _period: float = 1.0

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция возврата ссылки на экземпляр маршрута.
func get_route() -> PatrolRoute:
	return _route


## Функция возврата периода расписания в игровых минутах.
func get_period() -> float:
	return _period


## Функция возврата числа отрезков на маршруте.
func get_segment_count() -> int:
	return _segments.size()


## Функция снимка патруля в момент игрового времени [param time_minutes].
func sample(time_minutes: float) -> Sample:
	if _segments.is_empty():
		return Sample.new(Vector2.ZERO, 0, true, Vector2.ZERO)
	
	var t: float = fposmod(time_minutes - _route.start_offset_minutes, _period)
	for idx: int in _segments.size():
		var segment: Segment = _segments[idx]
		
		# Стоянка в начале отрезка.
		if t < _route.dwell_minutes:
			return Sample.new(segment.from, idx, true, segment.heading)
		t -= _route.dwell_minutes
		
		# Движение по отрезку.
		if t < segment.travel:
			var position: Vector2 = segment.from.lerp(segment.to, t / segment.travel)
			return Sample.new(position, idx, false, segment.heading)
		t -= segment.travel
	
	# Численный хвост периода - возвращаемся в начало.
	return Sample.new(_segments[0].from, 0, true, _segments[0].heading)

#endregion

#region REGULAR_PRIVATE

## Функция создания отрезка с [param from] до [param to] с длительностью по скорости.
func _make_segment(from: Vector2, to: Vector2) -> Segment:
	var travel: float = 0.0
	if _route.speed > 0.0:
		travel = Iso.ground_distance(from, to) / _route.speed
	return Segment.new(from, to, travel)


## Функция предвычисления отрезков и периода по маршруту.
func _build() -> void:
	_segments.clear()
	var points: Array[Vector2] = []
	for cell: Vector2i in _route.cells:
		points.append(Iso.cell_to_world(cell))
	
	# Одна клетка - стоянка на месте.
	if points.size() == 1:
		_segments.append(Segment.new(points[0], points[0], 0.0))
	else:
		# Туда.
		for idx: int in points.size() - 1:
			_segments.append(_make_segment(points[idx], points[idx + 1]))
		
		# Замыкание или обратно по тем же точкам.
		if _route.loop:
			_segments.append(_make_segment(points.back(), points.front()))
		else:
			for idx: int in range(points.size() - 1, 0, -1):
				_segments.append(_make_segment(points[idx], points[idx - 1]))
	
	# Период: каждый отрезок = стоянка + движение.
	_period = 0.0
	for segment: Segment in _segments:
		_period += _route.dwell_minutes + segment.travel
	if _period <= 0.0:
		_period = 1.0

#endregion

#region OVERRIDE_PRIVATE

## Функция инициализации.
func _init(p_route: PatrolRoute) -> void:
	_route = p_route
	if not is_instance_valid(_route) or _route.cells.is_empty():
		push_error("%s: empty route detected!" % to_string())
		_route = PatrolRoute.new([], 0.0, 0.0, true, 0.0)
		return
	_build()


## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion


#region CLASSES

## Отрезок маршрута.
class Segment extends RefCounted:
	#region VARIABLES
	#region REGULAR_PUBLIC
	
	## Откуда начинается отрезок маршрут.
	var from: Vector2 = Vector2.ZERO
	
	## Где заканчивается отрезок маршрута.
	var to: Vector2 = Vector2.ZERO
	
	## Длительность движения в игровых минутах (длина / speed).
	var travel: float = 0.0
	
	## Направление движения (нулевое для отрезка нулевой длины).
	var heading: Vector2 = Vector2.ZERO
	
	#endregion
	#endregion
	
	
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция инициализации.
	func _init(p_from: Vector2, p_to: Vector2, p_travel: float) -> void:
		from = p_from
		to = p_to
		travel = p_travel
		heading = (p_to - p_from).normalized()
	
	#endregion
	#endregion


## Снимок патруля в момент времени.
class Sample extends RefCounted:
	#region VARIABLES
	#region REGULAR_PUBLIC
	
	## Мировая позиция в px.
	var position: Vector2 = Vector2.ZERO
	
	## Индекс текущего сегмента [PatrolRecord.Segment].
	var segment: int = 0
	
	## Флаг стоянки на путевой точке.
	var dwelling: bool = false
	
	## Направление движения/взгляда.
	var heading: Vector2 = Vector2.ZERO
	
	#endregion
	#endregion
	
	
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция инициализации.
	func _init(
			p_position: Vector2, p_segment: int,
			p_dwelling: bool, p_heading: Vector2
	) -> void:
		position = p_position
		segment = p_segment
		dwelling = p_dwelling
		heading = p_heading
	
	#endregion
	#endregion

#endregion
