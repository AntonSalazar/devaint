extends TestCase

## Тесты [SignalLayer]: подгонка полигона под грид, параметры шейдера,
## текстура статики и ее обновление по OnStaticChanged, отписка.

#region CONSTANTS

## Сцена слоя.
const LAYER_SCENE: String = "res://core/signal_layer/signal_layer.tscn"

## Угол тестового грида.
const ORIGIN: Vector2i = Vector2i(-2, -2)

## Размер тестового грида.
const SIZE: Vector2i = Vector2i(5, 5)

#endregion


#region FUNCTIONS
#region OVERRIDE_PUBLIC

## Полный сброс шины перед каждым тестом.
func before_each() -> void:
	EventBus.reset()


## Сброс шины после теста.
func after_each() -> void:
	EventBus.reset()

#endregion

#region TESTS

## Полигон растянут на весь грид: все центры клеток внутри него.
func test_polygon_covers_grid() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	var layer: SignalLayer = _spawn_layer()
	layer.init(grid)
	
	check_eq(layer.polygon.size(), 4, "polygon is a quad")
	var outside: int = 0
	for x: int in range(ORIGIN.x, ORIGIN.x + SIZE.x):
		for y: int in range(ORIGIN.y, ORIGIN.y + SIZE.y):
			var center: Vector2 = Iso.cell_to_world(Vector2i(x, y))
			if not Geometry2D.is_point_in_polygon(center, layer.polygon):
				outside += 1
	check_eq(outside, 0, "every cell center lies inside the polygon")
	layer.deinit()
	layer.free()


## Шейдер получает геометрию грида и тайла из Iso.
func test_shader_geometry_parameters() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	var layer: SignalLayer = _spawn_layer()
	layer.init(grid)
	var shader: ShaderMaterial = layer.material as ShaderMaterial
	
	check_eq(shader.get_shader_parameter("grid_origin"), Vector2(ORIGIN), "grid_origin")
	check_eq(shader.get_shader_parameter("grid_size"), Vector2(SIZE), "grid_size")
	check_eq(
			shader.get_shader_parameter("tile_size"),
			Vector2(Iso.TILE_WIDTH, Iso.TILE_HEIGHT),
			"tile_size comes from Iso"
	)
	layer.deinit()
	layer.free()


## Текстура статики размером с грид и обновляется по OnStaticChanged.
func test_coverage_texture_updates() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	var layer: SignalLayer = _spawn_layer()
	layer.init(grid)
	var shader: ShaderMaterial = layer.material as ShaderMaterial
	
	var texture: ImageTexture = shader.get_shader_parameter("coverage_tex") as ImageTexture
	check_true(texture != null, "coverage_tex is an ImageTexture")
	if texture == null:
		layer.deinit()
		layer.free()
		return
	check_eq(texture.get_size(), Vector2(SIZE), "texture size equals grid size")
	var local: Vector2i = Vector2i.ZERO - ORIGIN
	check_near(texture.get_image().get_pixel(local.x, local.y).r, 0.0, "empty grid: pixel 0")
	
	grid.add_tower(Vector2i.ZERO, 300.0)
	texture = shader.get_shader_parameter("coverage_tex") as ImageTexture
	check_near(
			texture.get_image().get_pixel(local.x, local.y).r, 1.0,
			"after OnStaticChanged the tower pixel is 1.0"
	)
	layer.deinit()
	layer.free()


## После deinit слой не слушает шину.
func test_deinit_unsubscribes() -> void:
	var grid: SignalGrid = SignalGrid.new(ORIGIN, SIZE)
	var layer: SignalLayer = _spawn_layer()
	layer.init(grid)
	layer.deinit()
	
	var records_left: int = 0
	for records: Array in EventBus._ref.subs.values():
		records_left += records.size()
	check_eq(records_left, 0, "no subscription records after deinit")
	layer.free()

#endregion

#region REGULAR_PRIVATE

## Создание слоя из сцены (без добавления в дерево).
func _spawn_layer() -> SignalLayer:
	var scene: PackedScene = load(LAYER_SCENE)
	return scene.instantiate() as SignalLayer

#endregion
#endregion
