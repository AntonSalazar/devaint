class_name SignalLayer
extends Polygon2D

## Класс визуализации сетки сигналов (поля покрытия роя).
##
## Читает картинку статики из [SignalGrid] в текстуру шейдера.
## Картинка обновляется по [StaticGrid.OnStaticChanged] сообщению.


#region VARIABLES
#region REGULAR_PRIVATE

## Ссылка на экземпляр сетки сигналов роя.
var _grid: SignalGrid = null

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция инициализации.
func init(p_grid: SignalGrid) -> void:
	_grid = p_grid
	_fit_to_grid()
	_upload()
	EventBus.subscribe(SignalGrid.OnStaticChanged, _on_static_changed)


## Функция деинициализации.
func deinit() -> void:
	EventBus.unsubscribe(SignalGrid.OnStaticChanged, _on_static_changed)
	_grid = null

#endregion

#region REGULAR_PRIVATE

## Функция подгонки полигона под мировой прямоугольник грида.
func _fit_to_grid() -> void:
	var origin: Vector2i = _grid.get_origin()
	var size: Vector2i = _grid.get_size()
	var corners: Array[Vector2i] = [
			origin, origin + Vector2i(size.x, 0),
			origin + size, origin + Vector2i(0, size.y),
	]
	var rect: Rect2 = Rect2(Iso.cell_to_world(origin), Vector2.ZERO)
	for corner: Vector2i in corners:
		rect = rect.expand(Iso.cell_to_world(corner))
	rect = rect.grow(Iso.TILE_WIDTH)
	polygon = PackedVector2Array([
		rect.position,
		Vector2(rect.end.x, rect.position.y),
		rect.end,
		Vector2(rect.position.x, rect.end.y)
	])
	
	var sh_material: ShaderMaterial = material as ShaderMaterial
	sh_material.set_shader_parameter("grid_origin", Vector2(origin))
	sh_material.set_shader_parameter("grid_size", Vector2(size))
	sh_material.set_shader_parameter("tile_size", Vector2(Iso.TILE_WIDTH, Iso.TILE_HEIGHT))


## Функция заливки картинки сетки роя в текстуру шейдера.
func _upload() -> void:
	var sh_material: ShaderMaterial = material as ShaderMaterial
	sh_material.set_shader_parameter(
			"coverage_tex",
			ImageTexture.create_from_image(_grid.get_static_image())
	)


## Функция, вызываемая при получении сообщения [param message]
## о перестройке сети роя.
func _on_static_changed(_message: SignalGrid.OnStaticChanged) -> void:
	_upload()

#endregion
#endregion
