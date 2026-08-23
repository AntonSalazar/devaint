class_name PatrolPath
extends Path2D

## Узел маршрута патрул.
##
## Рисуется в редакторе как Path2D, точки кривой - путевые клетки.
## В игре превращается в [PatrolRoute].


#region VARIABLES
#region EXPORT_PUBLIC

## Скорость, мировых px за игровую минуту.
@export var speed: float = 400.0

## Стоянка на каждой путевой точке, игровых минут (например "сканирует").
@export var dwell_minutes: float = 0.5

## Флаг, является маршрут замкнутым. Если нет, то вернется обратно.
@export var loop: bool = true

## Сдвиг расписания, минут - чтобы два патруля на одном маршруте не шли строем.
@export var start_offset_minutes: float = 0.0

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция сборки данных маршрута из точке кривой.
## Мировые точки -> клетки, а подряд идущие дубли схлопываются.
func build_route() -> PatrolRoute:
	var cells: Array[Vector2i] = []
	for idx: int in curve.point_count:
		var cell: Vector2i = Iso.world_to_cell(to_global(curve.get_point_position(idx)))
		if cells.is_empty() or cells[-1] != cell:
			cells.append(cell)
	return PatrolRoute.new(cells, speed, dwell_minutes, loop, start_offset_minutes)

#endregion
#endregion
