class_name DebugOverlay
extends CanvasLayer

## Класс отображения отладочного слоя.
##
## Выводит техническую информацию для отладки.


#region VARIABLES
#region REGULAR_PRIVATE

## Ссылка на экземпляр таймера игрового времени.
var _clock: GameClock = null

## Ссылка на экземпляр робота.
var _robot: Robot = null

#endregion

#region ONREADY_PRIVATE

## Ссылка на экземпляр вывода.
@onready var _output: RichTextLabel = $Output

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция инициализации.
func init(clock: GameClock, robot: Robot) -> void:
	_clock = clock
	_robot = robot
	EventBus.subscribe(EventBus.Message, _on_message)
	set_process(true)


## Функция деинициализации.
func deinit() -> void:
	set_process(false)
	EventBus.unsubscribe(EventBus.Message, _on_message)
	_clock = null
	_robot = null

#endregion

#region REGULAR_PRIVATE

## Функция, вызываемая при получении сообщения [param message].
func _on_message(message: EventBus.Message) -> void:
	print("[%s] %s" % [_clock.get_datetime_str(), message.to_string()])

#endregion

#region OVERRIDE_PRIVATE

## Функция, вызываемая при первом кадре.
func _ready() -> void:
	set_process(false)


## Функция процессинга, где [param delta] - время между кадрами.
@warning_ignore("unused_parameter")
func _process(delta: float) -> void:
	_output.set_text(
			(
				"%s x%d progress %.04f" +
				"\nbattery %.1f%%" +
				"\nnotice %.1f%%"
			) %
			[
				_clock.get_datetime_str(),
				_clock.get_speed(),
				_clock.get_day_progress(),
				_robot.get_battery(),
				_robot.get_notice(),
			]
	)


## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
