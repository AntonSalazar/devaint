class_name World
extends Node2D

## Класс игрового мира.


#region VARIABLES
#region ONREADY_PRIVATE

## Ссылка на экземпляр маркера, где будет спавниться робот [Robot].
@onready var _robot_spawn: Marker2D = %RobotSpawn

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция возврата глобальных координат местоположения спавна робота [Robot].
func get_robot_spawn() -> Vector2:
	return _robot_spawn.get_global_position()

#endregion

#region OVERRIDE_PRIVATE

## Функция возврата представления экземпляра класса в виде строки.
func _to_string() -> String:
	return get_script().get_global_name()

#endregion
#endregion
