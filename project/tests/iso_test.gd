extends TestCase

## Тесты [Iso]: изометрическая поправка движения и согласованность констант.

#region FUNCTIONS
#region TESTS

## Горизонтальный ввод — полная скорость, без искажений.
func test_horizontal_input_full_speed() -> void:
	var direction: Vector2 = Iso.move_direction(Vector2.RIGHT)
	check_near(direction.x, 1.0, "horizontal: x = 1.0")
	check_near(direction.y, 0.0, "horizontal: y = 0.0")


## Вертикальный ввод — вдвое короче горизонтального.
func test_vertical_input_half_speed() -> void:
	var direction: Vector2 = Iso.move_direction(Vector2.DOWN)
	check_near(direction.x, 0.0, "vertical: x = 0.0")
	check_near(direction.y, Iso.SCALE_Y, "vertical: y = SCALE_Y")
	check_near(
			direction.length() * 2.0, Iso.move_direction(Vector2.RIGHT).length(),
			"vertical speed is half of horizontal"
	)


## Диагональный ввод ложится вдоль ребра тайла 2:1.
func test_diagonal_input_follows_tile_edge() -> void:
	var direction: Vector2 = Iso.move_direction(Vector2.ONE)
	check_near(direction.y / direction.x, 0.5, "diagonal slope is 2:1")
	
	var mirrored: Vector2 = Iso.move_direction(Vector2(-1.0, 1.0))
	check_near(mirrored.y / mirrored.x, -0.5, "mirrored diagonal slope is 2:1")


## Нулевой ввод — нулевой вектор, без NaN.
func test_zero_input_is_safe() -> void:
	var direction: Vector2 = Iso.move_direction(Vector2.ZERO)
	check_true(direction == Vector2.ZERO, "zero input gives a zero vector")
	check_true(direction.is_finite(), "no NaN in the result")


## Константы согласованы: сжатие вертикали следует из пропорции тайла.
func test_constants_are_consistent() -> void:
	check_near(
			float(Iso.TILE_HEIGHT) / float(Iso.TILE_WIDTH), Iso.SCALE_Y,
			"SCALE_Y equals TILE_HEIGHT / TILE_WIDTH"
	)


## Центры клеток бит-в-бит совпадают с TileMapLayer.map_to_local.
func test_cell_to_world_matches_godot() -> void:
	var layer: TileMapLayer = _make_layer()
	var mismatches: int = 0
	for x: int in range(-12, 13):
		for y: int in range(-12, 13):
			var cell: Vector2i = Vector2i(x, y)
			if Iso.cell_to_world(cell) != layer.map_to_local(cell):
				mismatches += 1
	check_eq(mismatches, 0, "cell_to_world matches map_to_local on a 25x25 area")
	layer.free()


## Точки внутри клеток попадают в ту же клетку, что и TileMapLayer.local_to_map.
func test_world_to_cell_matches_godot() -> void:
	var layer: TileMapLayer = _make_layer()
	var offsets: Array[Vector2] = [
		Vector2.ZERO,
		Vector2(20.0, 8.0), Vector2(-20.0, 8.0), Vector2(20.0, -8.0), Vector2(-20.0, -8.0),
		Vector2(40.0, 0.0), Vector2(-40.0, 0.0), Vector2(0.0, 20.0), Vector2(0.0, -20.0),
		Vector2(30.0, 14.0), Vector2(-33.0, -12.0), Vector2(7.0, -25.0), Vector2(-5.0, 27.0),
	]
	var mismatches: int = 0
	for x: int in range(-12, 13):
		for y: int in range(-12, 13):
			var center: Vector2 = Iso.cell_to_world(Vector2i(x, y))
			for offset: Vector2 in offsets:
				var point: Vector2 = center + offset
				if Iso.world_to_cell(point) != layer.local_to_map(point):
					mismatches += 1
	check_eq(mismatches, 0, "world_to_cell matches local_to_map for interior points")
	layer.free()


## Round-trip: клетка -> центр -> клетка без потерь.
func test_cell_round_trip() -> void:
	var mismatches: int = 0
	for x: int in range(-50, 51):
		for y: int in range(-50, 51):
			var cell: Vector2i = Vector2i(x, y)
			if Iso.world_to_cell(Iso.cell_to_world(cell)) != cell:
				mismatches += 1
	check_eq(mismatches, 0, "world_to_cell(cell_to_world(c)) == c")


## Расстояние по земле: горизонталь без изменений, экранная вертикаль разжата.
func test_ground_distance() -> void:
	check_near(
			Iso.ground_distance(Vector2.ZERO, Vector2(100.0, 0.0)), 100.0,
			"horizontal ground distance equals screen distance"
	)
	check_near(
			Iso.ground_distance(Vector2.ZERO, Vector2(0.0, 50.0)), 100.0,
			"vertical screen distance is stretched by 1 / SCALE_Y"
	)
	check_near(
			Iso.ground_distance(Vector2(10.0, 10.0), Vector2(10.0, 10.0)), 0.0,
			"same point gives zero"
	)


## Конвенция Godot закреплена: клетка (0,0) занимает прямоугольник
## от (0,0) до (TILE_WIDTH, TILE_HEIGHT).
func test_origin_cell_convention() -> void:
	check_eq(
			Iso.cell_to_world(Vector2i.ZERO),
			Vector2(Iso.TILE_WIDTH / 2.0, Iso.TILE_HEIGHT / 2.0),
			"cell (0,0) center is at half tile size"
	)
	check_eq(
			Iso.world_to_cell(Vector2(1.0, 1.0)), Vector2i(-1, 0),
			"top-left corner belongs to (-1,0)"
	)

#endregion

#region REGULAR_PRIVATE

## Настоящий TileMapLayer с изометрией проекта для сверки конверсий.
func _make_layer() -> TileMapLayer:
	var tile_set: TileSet = TileSet.new()
	tile_set.tile_shape = TileSet.TILE_SHAPE_ISOMETRIC
	tile_set.tile_layout = TileSet.TILE_LAYOUT_DIAMOND_DOWN
	tile_set.tile_size = Vector2i(Iso.TILE_WIDTH, Iso.TILE_HEIGHT)
	var layer: TileMapLayer = TileMapLayer.new()
	layer.tile_set = tile_set
	return layer

#endregion
#endregion
