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

## Ссылка на контейнер с маршрутами [PatrolPath].
@onready var _routes: Node2D = %Routes

## Ссылка на контейнер с вышками [TowerMarkers].
@onready var _towers: Node2D = %Towers

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


## Функция сборки маршрутов патрулей из узлов [member _routes].
func get_patrol_routes() -> Array[PatrolRoute]:
	var routes: Array[PatrolRoute] = []
	for child: Node in _routes.get_children():
		if child is PatrolPath:
			routes.append(child.build_route())
	return routes


## Функция возврата списка маркеров [TowerMarker], где будут находиться вышки.
func get_tower_markers() -> Array[TowerMarker]:
	var towers: Array[TowerMarker] = []
	for child: Node in _towers.get_children():
		if child is TowerMarker:
			towers.append(child)
	return towers

#endregion

#region OVERRIDE_PRIVATE

## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
