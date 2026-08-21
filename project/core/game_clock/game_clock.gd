class_name GameClock
extends RefCounted

## Класс игрового времени.
##
## Отвечает за тики симуляции.
## Всё что завязано на игровое время - будет высчитываться тут.


#region CONSTANTS

## Доступный набор множителей скорости.
## Во время геймплея будет доступно только пауза (x0) и x1.
## Остальные значения отведены для тестирования.
const SPEEDS: Array[int] = [0, 1, 2, 4, 8, 16, 32]

## Длительность игрового дня в секундах реального времени.
const DAY_DURATION: float = 3600.0

## Длительность игрового часа в секундах реального времени.
const HOUR_DURATION: float = DAY_DURATION / 24.0

## Длительность игровой минуты в секундах реального времени.
const MINUTE_DURATION: float = HOUR_DURATION / 60.0

## Количество минут в дне.
const MINUTES_PER_DAY: int = 24 * 60

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Общее игровое время в минутах.
var _total_minutes: int = 0

## Индекс скорости из [constant SPEEDS].
var _speed_id: int = 1

## Текущий множитель скорости.
var _speed: int = 1

## Аккумулятор времени. Вбирает в себя время в [method advance].
var _accumulator: float = 0.0

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция возврата текущего множителя скорости.
func get_speed() -> int:
	return _speed


## Функция установки скорости по индексу [param id].
func set_speed(id: int) -> void:
	_speed_id = clampi(id, 0, SPEEDS.size() - 1)
	_speed = SPEEDS[_speed_id]


## Функция смены скорости множителя времени по сдвигу [param offset].
func change_speed(offset: int) -> void:
	set_speed(_speed_id + offset)


## Функция возврата нормализованного прогресса игрового дня [0.0, 1.0).
## Пригодится шейдерам и всему тому, где нужно знать прогресс дня.
func get_day_progress() -> float:
	var minutes: int = _total_minutes % MINUTES_PER_DAY
	var result: float = (minutes + _accumulator / MINUTE_DURATION) / MINUTES_PER_DAY
	return result


## Функция вычисления игрового времени по общим минутам [param total_minutes]
## в формате словаря.
func compute_time(total_minutes: int) -> Dictionary:
	var result: Dictionary = {
		"minute": total_minutes % 60,
		"hour": int(total_minutes / 60.0) % 24,
		"day": 1 + int(total_minutes / float(MINUTES_PER_DAY)),
	}
	return result


## Функция возврата текущего времени в формате словаря.
func get_datetime() -> Dictionary:
	return compute_time(_total_minutes)


## Функция возврата текущего времени в виде строки.
func get_datetime_str() -> String:
	var datetime := get_datetime()
	var result: String = (
			"Day %d %02d:%02d" %
			[datetime.day, datetime.hour, datetime.minute]
	)
	return result


## Функция добавления игрового времени по дельте [param delta].
func advance(delta: float) -> void:
	# Копим время.
	_accumulator += delta * _speed
	
	# Проводим тики.
	while _accumulator >= MINUTE_DURATION:
		_accumulator -= MINUTE_DURATION
		
		# Сравним до и после.
		var prev_dt := get_datetime()
		_total_minutes += 1
		var crnt_dt := get_datetime()
		
		# Минуты точно сменились.
		OnMinutePassed.new(_total_minutes, crnt_dt).push()
		
		# Часы и дни уже будем сравнивать.
		if prev_dt.hour != crnt_dt.hour:
			OnHourPassed.new(_total_minutes, crnt_dt).push()
		if prev_dt.day != crnt_dt.day:
			OnDayPassed.new(_total_minutes, crnt_dt).push()

#endregion

#region OVERRIDE_PRIVATE

## Функция инициализации, где [param p_total_minutes] - время начала дня.
func _init(p_total_minutes: int = 0) -> void:
	_total_minutes = p_total_minutes


## Функция возврата представления экзепляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion


#region CLASSES
#region MESSAGES

## Класс-контейнер с данными о текущем времени события.
@abstract class TickData extends EventBus.Message:
	#region VARIABLES
	#region REGULAR_PUBLIC
	
	## Общее время в игровых минутах.
	var total_minutes: int = 0
	
	## Общее количество пройденных дней.
	var day: int = 0
	
	## Текущий час.
	var hour: int = 0
	
	## Текущая минута.
	var minute: int = 0
	
	#endregion
	#endregion
	
	
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция инициализации.
	func _init(p_total_minutes: int, p_datetime: Dictionary) -> void:
		total_minutes = p_total_minutes
		minute = p_datetime.minute
		hour = p_datetime.hour
		day = p_datetime.day
	
	#endregion
	#endregion


## Класс-событие тика минуты игрового времени.
class OnMinutePassed extends TickData:
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция возврата представления экземпляра класса в виде строки.
	func _to_string() -> String:
		return "GameClock.OnMinutePassed"
	
	#endregion
	#endregion


## Класс-событие тика часа игрового времени.
class OnHourPassed extends TickData:
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция возврата представления экземпляра класса в виде строки.
	func _to_string() -> String:
		return "GameClock.OnHourPassed"
	
	#endregion
	#endregion


## Класс-событие тика дня игрового времени.
class OnDayPassed extends TickData:
	#region FUNCTIONS
	#region OVERRIDE_PRIVATE
	
	## Функция возврата представления экземпляра класса в виде строки.
	func _to_string() -> String:
		return "GameClock.OnDayPassed"
	
	#endregion
	#endregion

#endregion
#endregion
