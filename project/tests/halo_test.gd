extends TestCase

## Тесты [Halo]: геометрия квада, параметры шейдера, монотонность по излучению,
## плавное схождение радиуса к цели, продвижение фазы пульса.

#region CONSTANTS

## Сцена ореола.
const HALO_SCENE: String = "res://core/halo/halo.tscn"

#endregion


#region FUNCTIONS
#region TESTS

## Первый вызов применяется мгновенно: квад — эллипсоидный бокс 2:1.
func test_first_emission_snaps_and_builds_quad() -> void:
	var halo: Halo = _spawn_halo()
	
	halo.set_emission(1.0)
	var radius: float = Halo.BASE_RADIUS + Halo.RADIUS_PER_EMISSION
	check_near(halo.get_radius(), radius, "first call snaps the radius")
	check_eq(halo.polygon.size(), 4, "quad has four points")
	var bounds: Rect2 = Rect2(halo.polygon[0], Vector2.ZERO)
	for point: Vector2 in halo.polygon:
		bounds = bounds.expand(point)
	check_near(bounds.size.x, radius * 2.0, "quad width is the diameter")
	check_near(bounds.size.y, radius * 2.0 * Iso.SCALE_Y, "quad height is squashed by SCALE_Y")
	check_near(bounds.get_center().x, 0.0, "quad is centered on x")
	check_near(bounds.get_center().y, 0.0, "quad is centered on y")
	halo.free()


## Параметры шейдера следуют из текущего состояния и Iso.
func test_shader_parameters() -> void:
	var halo: Halo = _spawn_halo()
	var shader: ShaderMaterial = halo.material as ShaderMaterial
	
	halo.set_emission(1.0)
	check_near(
			shader.get_shader_parameter("radius"),
			Halo.BASE_RADIUS + Halo.RADIUS_PER_EMISSION, "radius for emission 1.0"
	)
	check_near(halo.get_pulse_speed(), Halo.BASE_PULSE + Halo.PULSE_PER_EMISSION, "pulse 1.0")
	check_near(shader.get_shader_parameter("iso_scale_y"), Iso.SCALE_Y, "iso scale from Iso")
	halo.free()


## Больше излучения — больше целевой радиус и быстрее пульс.
func test_monotonic_in_emission() -> void:
	var halo: Halo = _spawn_halo()
	
	halo.set_emission(1.0)
	var radius_idle: float = halo.get_target_radius()
	var pulse_idle: float = halo.get_pulse_speed()
	halo.set_emission(3.0)
	halo._process(1.0)
	check_true(halo.get_target_radius() > radius_idle, "target radius grows")
	check_true(halo.get_pulse_speed() > pulse_idle, "pulse speeds up")
	halo.free()


## Смена излучения сходится к цели плавно, а не скачком.
func test_radius_converges_smoothly() -> void:
	var halo: Halo = _spawn_halo()
	var shader: ShaderMaterial = halo.material as ShaderMaterial
	
	halo.set_emission(1.0)
	var start: float = halo.get_radius()
	halo.set_emission(3.0)
	check_near(halo.get_radius(), start, "no jump right after the change")
	halo._process(0.1)
	check_near(halo.get_radius(), start + Halo.RADIUS_SPEED * 0.1, "moves at RADIUS_SPEED")
	check_true(halo.get_radius() < halo.get_target_radius(), "still short of the target")
	halo._process(1.0)
	check_near(halo.get_radius(), halo.get_target_radius(), "reaches the target")
	check_near(
			shader.get_shader_parameter("radius"), halo.get_target_radius(),
			"shader follows the current radius"
	)
	halo.free()


## Фаза пульса копится в скрипте и заворачивается в [0, 1).
func test_phase_advances_and_wraps() -> void:
	var halo: Halo = _spawn_halo()
	var shader: ShaderMaterial = halo.material as ShaderMaterial
	
	halo.set_emission(1.0)
	halo._process(0.25)
	check_near(shader.get_shader_parameter("phase"), 0.25, "phase = pulse_speed * dt")
	halo._process(1.0)
	var phase: float = shader.get_shader_parameter("phase")
	check_true(phase >= 0.0 and phase < 1.0, "phase wraps into [0, 1)")
	halo.free()

#endregion

#region REGULAR_PRIVATE

## Создание ореола из сцены (без добавления в дерево).
func _spawn_halo() -> Halo:
	var scene: PackedScene = load(HALO_SCENE)
	return scene.instantiate() as Halo

#endregion
#endregion
