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

#endregion
#endregion
