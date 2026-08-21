class_name Robot
extends CharacterBody2D

## Класс робота игрока.
##
## Умеет двигаться в изометрии, "замораживаться" на паузе.
## Потом обрастет всякой телесной симуляцией.


#region ENUMS

## Битовое перечисление состояний робота.
enum State {
	IDLE = 0x01, ## Стоит на месте.
	WALK = 0x02, ## Ходьба.
}

#endregion


#region CONSTANTS

## Скорость ходьбы в px/сек по-горизонтали.
const WALK_SPEED: float = 220.0

## Максимальный размер заряда батареи.
const BATTERY_MAX: float = 100.0

## Таблица расхода батареи, разбитые на состояния [enum State].
const DRAIN: Dictionary[State, float] = {
	State.IDLE: 0.02,
	State.WALK: 0.05,
}

#endregion


#region PROPERTIES
#region BATTERY

## Заряд батареи робота.
var _battery: float = 0.0:
	get = get_battery

#endregion
#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Ссылка на экземпляр таймера игрового времени.
var _clock: GameClock = null

## Секунды реального времени с прошлого игрового минутного тика.
## Необходимы для вычисления разряда батареи. Разбито по состояниям.
var _seconds: Dictionary[State, float] = {}

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Геттер заряда батареи робота.
func get_battery() -> float:
	return _battery


## Функция инициализации.
func init(clock: GameClock) -> void:
	_clock = clock
	_battery = BATTERY_MAX
	EventBus.subscribe(GameClock.OnMinutePassed, _on_minute_passed)
	set_physics_process(true)


## Функция деинициализации.
func deinit() -> void:
	set_physics_process(false)
	EventBus.unsubscribe(GameClock.OnMinutePassed, _on_minute_passed)
	_battery = 0.0
	_seconds.clear()
	_clock = null


#endregion

#region REGULAR_PRIVATE

## Функция, вызываемая при получении сообщения [param message]
## о наступлении новой минуты игры.
func _on_minute_passed(_message: GameClock.OnMinutePassed) -> void:
	# Посчитаем общее время затраты.
	var total: float = 0.0
	for seconds: float in _seconds.values():
		total += seconds
	
	# Смотрим, что накопление > 0.0.
	if is_zero_approx(total) or total < 0.0:
		return
	
	# Определим общий расход батареи.
	var drain: float = 0.0
	for state: State in _seconds:
		var seconds: float = _seconds[state] / total
		drain += DRAIN.get(state, 0.0) * seconds
	_battery = clampf(_battery - drain, 0.0, BATTERY_MAX)
	
	# Готово, можно затирать расходы за пройденное время.
	_seconds.clear()

#endregion

#region OVERRIDE_PRIVATE

## Функция, вызываемая при первом кадре.
func _ready() -> void:
	set_physics_process(false)


## Функция процессинга, где [param delta] - фиксированное время между кадрами.
func _physics_process(delta: float) -> void:
	# Если время не задано или на паузе - пропускаем.
	if not is_instance_valid(_clock) or _clock.is_paused():
		velocity = Vector2.ZERO
		return
	
	# Делаем движение.
	var input: Vector2 = Input.get_vector("move_left", "move_right", "move_up", "move_down")
	velocity = Iso.move_direction(input) * WALK_SPEED
	move_and_slide()
	
	# Теперь копим секунды текущего состояния.
	var state: State = State.IDLE if velocity.is_zero_approx() else State.WALK
	_seconds[state] = _seconds.get(state, 0.0) + delta


## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
