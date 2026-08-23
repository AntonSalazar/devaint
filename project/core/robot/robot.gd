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

## Таблица расхода батареи, разбитая на состояния [enum State].
const DRAIN: Dictionary[State, float] = {
	State.IDLE: 0.02,
	State.WALK: 0.05,
}

## Таблица излучения, разбитая на состояния [enum State].
const EMISSION: Dictionary[State, float] = {
	State.IDLE: 1.0,
	State.WALK: 3.0,
}

## Маска для локомоции. Всегда поднят хотя бы один из этих флагов.
const LOCOMOTION_MASK: int = State.IDLE | State.WALK

#endregion


#region PROPERTIES
#region STATE

## Битовая маска состояний [enum STATE].
var _flags: int = State.IDLE:
	set = _set_flags,
	get = get_flags

#endregion
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

## Ссылка на экземпляр сети роя.
var _grid: SignalGrid = null

## Секунды реального времени с прошлого игрового минутного тика.
## Необходимы для вычисления разряда батареи. Разбито по состояниям.
var _seconds: Dictionary[State, float] = {}

#endregion
#region ONREADY_PRIVATE

## Ссылка на экземляр отрисовки излучения.
@onready var _halo: Halo = %Halo

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Геттер битовой маски состояния робота по [enum State].
func get_flags() -> int:
	return _flags


## Функция проверки, содержится ли бит [param flag]
## в маске состояния [member _flags].
func has_flag(flag: State) -> bool:
	return _flags & flag != 0


## Геттер заряда батареи робота.
func get_battery() -> float:
	return _battery


## Функция возврата суммарного излучения робота
## по его текущему состоянию [member _flags], ед/сек.
func get_emission() -> float:
	var emission: float = 0.0
	for flag: State in State.values():
		if has_flag(flag):
			emission += EMISSION.get(flag, 0.0)
	return emission


## Функция возврата уровня тревоги роя по позиции робота.
func get_notice() -> float:
	return _grid.get_notice_at(global_position)


## Функция инициализации.
func init(clock: GameClock, grid: SignalGrid) -> void:
	_clock = clock
	_grid = grid
	_battery = BATTERY_MAX
	_halo.set_emission(get_emission())
	EventBus.subscribe(GameClock.OnMinutePassed, _on_minute_passed)
	set_physics_process(true)


## Функция деинициализации.
func deinit() -> void:
	set_physics_process(false)
	EventBus.unsubscribe(GameClock.OnMinutePassed, _on_minute_passed)
	_battery = 0.0
	_seconds.clear()
	_grid = null
	_clock = null

#endregion

#region REGULAR_PRIVATE

## Сеттер битовой маски состояния робота по [enum State].
func _set_flags(flags: int) -> void:
	if _flags == flags:
		return
	
	# Обновляем маску и рисуем излучение.
	_flags = flags
	_halo.set_emission(get_emission())


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
	
	# Локомоция занимает свою часть маски, модификаторы сохраняются.
	var locomotion: int = State.IDLE if velocity.is_zero_approx() else State.WALK
	_set_flags((_flags & ~LOCOMOTION_MASK) | locomotion)
	
	# Теперь копим секунды текущего состояния.
	for flag: State in State.values():
		if _flags & flag:
			_seconds[flag] = _seconds.get(flag, 0.0) + delta
	
	# Теперь вписываем свой след в память роя (игровые минуты из реального времени).
	_grid.accumulate(
			global_position, get_emission(),
			delta * _clock.get_speed() / GameClock.MINUTE_DURATION
	)


## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
