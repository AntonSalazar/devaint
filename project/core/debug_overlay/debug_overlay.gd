class_name DebugOverlay
extends CanvasLayer

## Класс отображения отладочного слоя.
##
## Выводит техническую информацию для отладки.


#region CONSTANTS

## Цвета уровней тревоги.
const NOTICE_LEVEL_COLOR: Dictionary[SignalGrid.Level, Color] = {
	SignalGrid.Level.NONE: Color("#4de6d9"),
	SignalGrid.Level.CURIOUS: Color("yellow"),
	SignalGrid.Level.SCOUT: Color("orange"),
	SignalGrid.Level.HUNT: Color("red"),
	SignalGrid.Level.ALARM: Color("red"),
}

## Сколько последний строк лога держим на экране.
const LOG_LINES: int = 8

## Список исключения сообщений.
const LOG_SKIP: Array[GDScript] = [GameClock.OnMinutePassed]

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Ссылка на экземпляр таймера игрового времени.
var _clock: GameClock = null

## Ссылка на экземпляр робота.
var _robot: Robot = null

## Кольцевой буффер строк лога, старые - в начале, новые - в конце.
var _log: Array[String] = []

#endregion

#region ONREADY_PRIVATE

## Ссылка на экземпляр вывода.
@onready var _output: RichTextLabel = %Output

## Ссылка на экземпляр прогресс бара значения тревоги.
@onready var _notice_bar: ProgressBar = %NoticeBar

## Ссылка на экземпляр вывода уровня тревоги.
@onready var _notice_level: Label = %NoticeLevel

## Ссылка на экземпляр вывода лога.
@onready var _log_output: RichTextLabel = %Log

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
	_log.clear()
	_render_log()

#endregion

#region REGULAR_PRIVATE

## Функция отрисовки лога сообщений.
func _render_log() -> void:
	_log_output.set_text("\n".join(_log))


## Функция, вызываемая при получении сообщения [param message].
func _on_message(message: EventBus.Message) -> void:
	var line: String = "[%s] %s" % [_clock.get_datetime_str(), message.to_string()]
	print(line)
	
	# Пропускаем шумные сообщения.
	for skip: GDScript in LOG_SKIP:
		if is_instance_of(message, skip):
			return
	
	# Рисуем лог.
	_log.append(line)
	if _log.size() > LOG_LINES:
		_log.pop_front()
	_render_log()

#endregion

#region OVERRIDE_PRIVATE

## Функция, вызываемая при первом кадре.
func _ready() -> void:
	set_process(false)


## Функция процессинга, где [param delta] - время между кадрами.
@warning_ignore("unused_parameter")
func _process(delta: float) -> void:
	var notice: float = _robot.get_notice()
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
				notice,
			]
	)
	
	# Обновим бар.
	var level: SignalGrid.Level = _robot.get_notice_level()
	_notice_bar.set_value(notice)
	_notice_bar.modulate = NOTICE_LEVEL_COLOR[level]
	_notice_level.set_text(
			"NOTICE %.0f%% %s" %
			[notice, SignalGrid.Level.find_key(level)]
	)


## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
