extends SceneTree

## Управляемый скриншот. Запуск: `just shot-drive`.
##
## Поднимает main.tscn, разгоняет время и ведет робота input-действиями
## к вышке, пока Movie Maker пишет кадры: на последних кадрах видны
## заполненная шкала заметности, уровень и лог. Движение — через
## Input.action_press, время — через InputEventAction в очередь ввода.

#region CONSTANTS

## Кадр, на котором разгоняем время.
const SPEED_UP_FRAME: int = 5

## Сколько раз нажать ускорение (x2 -> x4 -> x8 -> x16).
const SPEED_UP_PRESSES: int = 4

## Кадр, с которого ведем робота.
const DRIVE_FROM_FRAME: int = 10

## Клетка вышки-цели (первая заглушка Main).
const TARGET_CELL: Vector2i = Vector2i(4, 12)

## Дистанция остановки у цели, px.
const ARRIVE_DISTANCE: float = 24.0

## Действия движения по осям экрана.
const MOVE_ACTIONS: Dictionary[String, Vector2] = {
	"move_right": Vector2.RIGHT,
	"move_left": Vector2.LEFT,
	"move_down": Vector2.DOWN,
	"move_up": Vector2.UP,
}

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Корень игры.
var _main: Main = null

## Счетчик кадров.
var _frame: int = 0

#endregion
#endregion


#region FUNCTIONS
#region OVERRIDE_PRIVATE

func _initialize() -> void:
	_main = (load("res://core/main/main.tscn") as PackedScene).instantiate()
	root.add_child(_main)


func _process(_delta: float) -> bool:
	_frame += 1
	if _frame == SPEED_UP_FRAME:
		for _idx: int in SPEED_UP_PRESSES:
			_press("time_speed_up")
	if _frame >= DRIVE_FROM_FRAME:
		_drive()
	return false

#endregion

#region REGULAR_PRIVATE

## Функция нажатия действия [param action] через очередь ввода (для _unhandled_input).
func _press(action: String) -> void:
	var event: InputEventAction = InputEventAction.new()
	event.action = action
	event.pressed = true
	Input.parse_input_event(event)


## Функция ведения робота к TARGET_CELL зажатием действий движения.
func _drive() -> void:
	var robot: Robot = _main.get_robot()
	if robot == null:
		return
	var delta: Vector2 = Iso.cell_to_world(TARGET_CELL) - robot.global_position
	for action: String in MOVE_ACTIONS:
		var axis: Vector2 = MOVE_ACTIONS[action]
		var wants: bool = delta.length() > ARRIVE_DISTANCE and delta.dot(axis) > ARRIVE_DISTANCE * 0.5
		if wants:
			Input.action_press(action)
		else:
			Input.action_release(action)

#endregion
#endregion
