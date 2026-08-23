class_name PatrolRoute
extends RefCounted

## Контейнер данных о маршруте.


#region VARIABLES
#region REGULAR_PUBLIC

## Ячейки путей.
var cells: Array[Vector2i] = []

## Скорость, мировых px за игровую минуту.
var speed: float = 400.0

## Стоянка на каждой путевой точке, игровых минут (например "сканирует").
var dwell_minutes: float = 0.5

## Флаг, является маршрут замкнутым. Если нет, то вернется обратно.
var loop: bool = true

## Сдвиг расписания, минут - чтобы два патруля на одном маршруте не шли строем.
var start_offset_minutes: float = 0.0

#endregion
#endregion


## Функция инициализации.
func _init(
		p_cells: Array[Vector2i], p_speed: float,
		p_dwell_minutes: float, p_loop: bool,
		p_start_offset_minutes: float
) -> void:
	cells = p_cells
	speed = p_speed
	dwell_minutes = p_dwell_minutes
	loop = p_loop
	start_offset_minutes = p_start_offset_minutes
