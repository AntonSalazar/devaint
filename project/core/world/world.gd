class_name World
extends Node2D

## Класс игрового мира.


#region VARIABLES
#region ONREADY_PRIVATE

## Ссылка на экземпляр маркера, где будет спавниться робот [Robot].
@onready var _robot_spawn: Marker2D = %RobotSpawn

## Ссылка на экземпляр земли в виде [TileMapLayer].
@onready var _ground: TileMapLayer = %Ground

## Ссылка на экземпляр отрисовки сетки роя.
@onready var _signal_layer: SignalLayer = %SignalLayer

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция возврата глобальных координат местоположения спавна робота [Robot].
func get_robot_spawn() -> Vector2:
	return _robot_spawn.get_global_position()


## Функция возврата прямоугольника покрашенных клеток земли.
func get_cell_rect() -> Rect2i:
	return _ground.get_used_rect()


## Функция возврата ссылки на экземляр отрисовки сетки роя.
func get_signal_layer() -> SignalLayer:
	return _signal_layer

#endregion

#region OVERRIDE_PRIVATE

## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
