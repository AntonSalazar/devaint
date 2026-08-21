class_name Main
extends Node

## Класс Main игры.
##
## Оправная точка, откуда всё начинается и где всё заканчивается.


#region CONSTANTS

## Время в игровых минутах, с которых начинается игра.
## 480m -> 8h.
const START_MINUTES: int = 480

## Максимальная дельта времени.
## На случай лагов игры или накопления дельты со стороны движка.
const MAX_FRAME_DELTA: float = 1.0

## Ссылка на префаб робота игрока [Robot].
const ROBOT_SCENE: PackedScene = preload("uid://jwkot7562ai0")

#endregion


#region PROPERTIES
#region GAME_CLOCK

## Ссылка на экземпляр игровых часов.
var _clock: GameClock = null:
	get = get_clock

#endregion
#region WORLD

## Ссылка на экземпляр мира.
var _world: World = null:
	get = get_world

#endregion
#region ROBOT

## Ссылка на экземпляр робота игрока.
var _robot: Robot = null:
	get = get_robot

#endregion
#endregion


#region VARIABLES
#region ONREADY_PRIVATE

## Ссылка на экземпляр вывода отладки.
@onready var _debug_overlay: DebugOverlay = %DebugOverlay

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Геттер ссылки на экземпляр игровых часов.
func get_clock() -> GameClock:
	return _clock


## Геттер ссылки на экземпляр мира.
func get_world() -> World:
	return _world


## Геттер ссылки на экземпляр робота игрока.
func get_robot() -> Robot:
	return _robot

#endregion

#region REGULAR_PRIVATE

## Функция завершения работы цикла [Main].
func _teardown() -> void:
	# Фильтруем повторное выключение.
	if not is_instance_valid(_clock):
		return
	
	print("%s: Teardown... Bye-bye!" % to_string())
	
	# Сбрасываем шину.
	EventBus.reset()
	
	# Отключаем процессинг.
	set_process(false)
	set_process_unhandled_input(false)
	
	# Сбрасываем ссылки.
	_debug_overlay.deinit()
	_robot.deinit()
	_robot.queue_free()
	_robot = null
	_world = null
	_clock = null


## Функция запуска работы цикла.
func _launch() -> void:
	# Создаем экземпляр таймера.
	_clock = GameClock.new(START_MINUTES)
	
	# Цепляем мир.
	_world = %World
	
	# Добавляем игрока.
	_robot = ROBOT_SCENE.instantiate()
	_world.add_child(_robot)
	_robot.set_global_position(_world.get_robot_spawn())
	_robot.init(_clock)
	
	# Добавляем отладочный гуй.
	_debug_overlay.init(_clock, _robot)
	
	# Запускаем процессинг.
	set_process(true)
	set_process_unhandled_input(true)

#endregion

#region OVERRIDE_PRIVATE

## Функция, вызываемая при первом кадре.
func _ready() -> void:
	set_process(false)
	set_process_unhandled_input(false)
	_launch()


## Функция, вызываемая при получении уведомления [param what] от движка.
func _notification(what: int) -> void:
	match what:
		NOTIFICATION_WM_CLOSE_REQUEST:
			_teardown()


## Функция процессинга, где [param delta] - время между кадрами.
func _process(delta: float) -> void:
	_clock.advance(minf(delta, MAX_FRAME_DELTA))


## Функция, вызываемая при получении необработанного ввода [param event].
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("time_pause"):
		_clock.toggle_pause()
	elif event.is_action_pressed("time_speed_up"):
		_clock.change_speed(+1)
	elif event.is_action_pressed("time_speed_down"):
		_clock.change_speed(-1)


## Функция, вызываемая при выходе из дерева.
func _exit_tree() -> void:
	_teardown()


## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
