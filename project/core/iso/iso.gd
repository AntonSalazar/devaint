class_name Iso
extends RefCounted

## Статичный класс математики изометрии.
##
## Единая точка в коде проекта, где вычисляются мат. операции, связанные с изометрией.


#region CONSTANTS

## Ширина тайла в px.
const TILE_WIDTH: int = 128

## Высота тайла в px.
const TILE_HEIGHT: int = 64

## Изометрический сдвиг по высоте в пропорции 2:1.
const SCALE_Y: float = 0.5

#endregion


#region FUNCTIONS
#region STATIC_PUBLIC

## Статичная функция возврата вектора скорости по вводу [param input]
## вводящей изометрическую поправку по вертикали [constant ISO_SCALE_Y].
static func move_direction(input: Vector2) -> Vector2:
	var direction: Vector2 = input.normalized()
	direction.y *= SCALE_Y
	return direction


## Статичная функция возврата центра клетки [param cell] в мировых координатах.
## Конвенция Godot для Diamond Down: клетка (0,0)
## лежит в прямоугольнике от (0,0) до (TILE_WIDTH, TILE_HEIGHT).
static func cell_to_world(cell: Vector2i) -> Vector2:
	var half_width: float = TILE_WIDTH * 0.5
	var half_height: float = TILE_HEIGHT * 0.5
	return Vector2(
			(cell.x - cell.y) * half_width + half_width,
			(cell.x + cell.y) * half_height + half_height,
	)


## Статичная функция возврата клетки,
## содержащей в себе мировую координату [param position].
## На ребрах и вершинах ромбов - округление к ближайшему центру.
static func world_to_cell(position: Vector2) -> Vector2i:
	var x: float = position.x / TILE_WIDTH + position.y / TILE_HEIGHT - 1.0
	var y: float = position.y / TILE_HEIGHT - position.x / TILE_WIDTH
	return Vector2i(roundi(x), roundi(y))


## Статичная функция возврата расстояния между мировыми точками [param a] и [param b],
## где вертикаль сжата по [constant SCALE_Y].
static func ground_distance(a: Vector2, b: Vector2) -> float:
	var delta: Vector2 = b - a
	var result: Vector2 = delta
	result.y /= SCALE_Y
	return result.length()

#endregion
#endregion
