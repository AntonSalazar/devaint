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

#endregion
#endregion
