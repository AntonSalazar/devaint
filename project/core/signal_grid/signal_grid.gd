class_name SignalGrid
extends RefCounted

## Класс сетки сигналов роя.
##
## Хранит покрытие роя в виде массива [member _static] и отвечает на вопрос:
## "Какая плотность покрытия в этой точке?".
## Одна структура данных для симуляции и отрисовки.


#region CONSTANTS

## Размер сектора в клетках.
const SECTOR_SIZE: int = 8

## Максимальное значение тревожности сектора в процентах.
const NOTICE_MAX: float = 100.0

## Единица тревожности.
const NOTICE_RATE: float = 2.5

## Спад тревожности по минутным тикам.
const DECAY_PER_MINUTE: float = 1.5

## Спад тревожности по минутным тикам там, где нет игрока.
const ABSENT_DECAY_MULTIPLIER: float = 2.0

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Клетка верхнего-левого угла грида.
var _origin: Vector2i = Vector2i.ZERO

## Размер грида в клетках.
var _size: Vector2i = Vector2i.ZERO

## Слой статики: суммарное покрытие работающих вышек, [0.0, 1.0] на клетку.
var _static: PackedFloat32Array = PackedFloat32Array()

## Список вышек, где индекс в массиве - это id вышки.
var _towers: Array[Tower] = []

## Таблица заметности игрока, где ключ - координата сектора,
## а значение - заметность 0 -> 100.
var _notice: Dictionary[Vector2i, float] = {}

## Сектор, где источник был последним. В этом месте спад будет медленней.
var _active_sector: Vector2i = Vector2i.ZERO

## Ссылка на закэшированную карту покрытия.
var _static_cache: Image = null

#endregion
#endregion


#region FUNCTIONS
#region STATIC_PUBLIC

## Статичная функция конвертации координат клетки грида [param cell] в сектор.
static func cell_to_sector(cell: Vector2i) -> Vector2i:
	return Vector2i(
			floori(cell.x / float(SECTOR_SIZE)),
			floori(cell.y / float(SECTOR_SIZE))
	)

#endregion

#region REGULAR_PUBLIC

## Функция возврата клетки верхнего-левого угла грида.
func get_origin() -> Vector2i:
	return _origin


## Функция возврата размера грида в клетках.
func get_size() -> Vector2i:
	return _size


## Функция возврата сектора по глобальной координате [param position].
func get_sector_at(position: Vector2) -> Vector2i:
	var cell: Vector2i = Iso.world_to_cell(position)
	var sector: Vector2i = cell_to_sector(cell)
	return sector


## Функция возврата процента заметности в секторе [param sector].
func get_notice(sector: Vector2i) -> float:
	return _notice.get(sector, 0.0)


## Функция возврата процента заметности по глобальной координате [param position].
func get_notice_at(position: Vector2) -> float:
	return get_notice(get_sector_at(position))


## Функция возврата снимка всех секторов.
## Пригодится при постройке карты.
func get_notices() -> Dictionary[Vector2i, float]:
	return _notice.duplicate()


## Функция инициализации.
func init() -> void:
	EventBus.subscribe(GameClock.OnMinutePassed, _on_minute_passed)


## Функция деинициализации.
func deinit() -> void:
	EventBus.unsubscribe(GameClock.OnMinutePassed, _on_minute_passed)


## Функция накопления заметности по излучению [param emission]
## в позиции мировых координат [param position] по минутам [param minutes].
func accumulate(position: Vector2, emission: float, minutes: float) -> void:
	var sector: Vector2i = get_sector_at(position)
	var gain: float = emission * get_coverage_at(position) * NOTICE_RATE * minutes
	_active_sector = sector
	if is_zero_approx(gain):
		return
	_notice[sector] = minf(_notice.get(sector, 0.0) + gain, NOTICE_MAX)


## Функция добавления активной вышки в клетке [param cell]
## с радиусом работы [param radius] в мировых координатах.
## Вернет id вышки.
func add_tower(cell: Vector2i, radius: float) -> int:
	if _cell_to_index(cell) < 0:
		push_warning("%s.add_tower: `cell:%s` out of grid." % [to_string(), cell])
	_towers.append(Tower.new(cell, radius))
	_rebuild_static()
	return _towers.size() - 1


## Функция установки флага работы [param active] вышки [Tower]
## по индексу [param id].
func set_tower_active(id: int, active: bool) -> void:
	if id < 0 or id >= _towers.size():
		push_error(
				"%s.set_tower_active: unknown tower `id:%d`" %
				[to_string(), id]
		)
		return
	
	# Отсеем, если флаг не изменился.
	var tower: Tower = _towers[id]
	if tower.active == active:
		return
	
	# Ставим флаг и перестраиваем сеть.
	tower.active = active
	_rebuild_static()


## Функция возврата плотности покрытия клетки [param cell] в диапазоне [0.0, 1.0].
func get_coverage(cell: Vector2i) -> float:
	var idx: int = _cell_to_index(cell)
	return _static[idx] if idx >= 0 else 0.0


## Функция возврата плотности покрытия в мировых координатах [param position]
## в диапазоне [0.0, 1.0].
func get_coverage_at(position: Vector2) -> float:
	return get_coverage(Iso.world_to_cell(position))


## Функция выгрузки слоя статики в картинку [constant Image.FORMAT_RF]
## размером с грид, где: R - покрытие клетки. Пригодится для шейдеров и карт.
func get_static_image() -> Image:
	# Возьмем с кэша.
	if is_instance_valid(_static_cache):
		return _static_cache
	
	# Создаем новую карту.
	var image: Image = Image.create_empty(_size.x, _size.y, false, Image.FORMAT_RF)
	for idx: int in _static.size():
		@warning_ignore("integer_division")
		image.set_pixel(idx % _size.x, idx / _size.x, Color(_static[idx], 0.0, 0.0))
	
	# Закэшируем и вернем.
	_static_cache = image
	return _static_cache

#endregion

#region REGULAR_PRIVATE

## Функция возврата индекса клетки [param cell] в слоях.
## Вернет -1, если клетка вне грида.
func _cell_to_index(cell: Vector2i) -> int:
	var local: Vector2i = cell - _origin
	if local.x < 0 or local.y < 0 or local.x >= _size.x or local.y >= _size.y:
		return -1
	return local.y * _size.x + local.x


## Функция возврата клетки по индексу [param idx] в слоях.
func _index_to_cell(idx: int) -> Vector2i:
	@warning_ignore("integer_division")
	return _origin + Vector2i(idx % _size.x, idx / _size.x)


## Функция перестройки сети роя по активным вышкам.
## Поле вышки - линейный спад от 1.0 в центре до 0.0 на радиусе,
## расстояние считается по земле (по изометрии), сумма ограничивается до 1.0.
func _rebuild_static() -> void:
	# Заполним нулями.
	_static.fill(0.0)
	
	# Пройдемся по активным вышкам.
	for tower: Tower in _towers:
		if not tower.active:
			continue
		
		# Вычисляем покрытие.
		var center: Vector2 = Iso.cell_to_world(tower.cell)
		for idx: int in _static.size():
			var point: Vector2 = Iso.cell_to_world(_index_to_cell(idx))
			var value: float = 1.0 - Iso.ground_distance(center, point) / tower.radius
			_static[idx] = minf(_static[idx] + maxf(value, 0.0), 1.0)
	
	# Вещаем, что сетка перестроена.
	_static_cache = null
	OnStaticChanged.new().push()


## Функция, вызываемая при получении сообщения [param message]
## о наступления новой игровой минуты.
func _on_minute_passed(_message: GameClock.OnMinutePassed) -> void:
	# Пройдемся по тревожным секторам и определим спад в них.
	var empty: Array[Vector2i] = []
	for sector: Vector2i in _notice:
		var decay: float = ABSENT_DECAY_MULTIPLIER * DECAY_PER_MINUTE
		if sector == _active_sector:
			decay = DECAY_PER_MINUTE
		var value: float = _notice[sector]
		value = maxf(value - decay, 0.0)
		_notice[sector] = value
		if is_zero_approx(value):
			empty.append(sector)
	
	# Удаляем пустые сектора.
	for sector: Vector2i in empty:
		_notice.erase(sector)

#endregion

#region OVERRIDE_PRIVATE

## Функция инициализации.
func _init(p_origin: Vector2i, p_size: Vector2i) -> void:
	_origin = p_origin
	_size = p_size
	_static.resize(_size.x * _size.y)
	_static.fill(0.0)


## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion


#region CLASSES

## Класс вышки роя - источник статического покрытия.
class Tower extends RefCounted:
	#region VARIABLES
	#region REGULAR_PUBLIC
	
	## Клетка, на которой стоит вышка.
	var cell: Vector2i = Vector2i.ZERO
	
	## Радиус действия вышки в мировых координатах (по земле).
	var radius: float = 0.0
	
	## Флаг работы вышки.
	var active: bool = true
	
	#endregion
	#endregion
	
	
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция инициализации.
	func _init(p_cell: Vector2i, p_radius: float) -> void:
		cell = p_cell
		radius = p_radius
	
	
	## Функция возврата представления экземпляра класса в виде строки.
	func _to_string() -> String:
		return "Tower(%d,%d)" % [cell.x, cell.y]
	
	#endregion
	#endregion


class OnStaticChanged extends EventBus.Message:
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция возврата представления экземпляра класса в виде строки.
	func _to_string() -> String:
		return "SignalGrid.OnStaticChanged"
	
	#endregion
	#endregion

#endregion
