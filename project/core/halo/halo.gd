class_name Halo
extends Polygon2D

## Класс ореола излучения: пульсирующее кольцо на земле вокруг носителя.
##
## Радиус и частота пульса растут с излучением и меняются плавно:
## цель задается через [method set_emission], текущие значения
## сходятся к ней в [method _process]. Фаза пульса копится здесь же,
## чтобы смена частоты не телепортировала кольцо.


#region CONSTANTS

## Базовый радиус ореола по горизонтали в px.
const BASE_RADIUS: float = 60.0

## Прирост радиуса на единицу излучения в px.
const RADIUS_PER_EMISSION: float = 30.0

## Базовая частота пульса, пульсов/сек.
const BASE_PULSE: float = 0.5

## Прирост частоты на единицу излучения.
const PULSE_PER_EMISSION: float = 0.5

## Скорость схождения радиуса к цели, px/сек.
const RADIUS_SPEED: float = 240.0

## Скорость схождения частоты пульса к цели, (пульсов/сек)/сек.
const PULSE_RATE: float = 2.0

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Текущий радиус, px.
var _radius: float = BASE_RADIUS

## Целевой радиус, px.
var _target_radius: float = BASE_RADIUS

## Текущая частота пульса, пульсов/сек.
var _pulse_speed: float = BASE_PULSE

## Целевая частота пульса, пульсов/сек.
var _target_pulse_speed: float = BASE_PULSE

## Фаза пульса [0.0, 1.0).
var _phase: float = 0.0

## Радиус, под который построен полигон (чтобы не перестраивать зря).
var _applied_radius: float = -1.0

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция установки излучения [param emission]: задает цель радиуса и пульса.
## Первый вызов (полигона еще нет) применяется мгновенно, дальше - плавно.
func set_emission(emission: float) -> void:
	_target_radius = BASE_RADIUS + RADIUS_PER_EMISSION * emission
	_target_pulse_speed = BASE_PULSE + PULSE_PER_EMISSION * emission
	if polygon.is_empty():
		_radius = _target_radius
		_pulse_speed = _target_pulse_speed
	_apply()


## Геттер текущего радиуса, px.
func get_radius() -> float:
	return _radius


## Геттер целевого радиуса, px.
func get_target_radius() -> float:
	return _target_radius


## Геттер текущей частоты пульса, пульсов/сек.
func get_pulse_speed() -> float:
	return _pulse_speed

#endregion

#region REGULAR_PRIVATE

## Функция применения текущих значений к полигону и шейдеру.
func _apply() -> void:
	# Полигон перестраиваем только при смене радиуса.
	if not is_equal_approx(_applied_radius, _radius):
		var half: Vector2 = Vector2(_radius, _radius * Iso.SCALE_Y)
		polygon = PackedVector2Array([
				-half, Vector2(half.x, -half.y),
				half, Vector2(-half.x, half.y),
		])
		_applied_radius = _radius
	
	# Шейдер.
	var sh_material: ShaderMaterial = material as ShaderMaterial
	sh_material.set_shader_parameter("radius", _radius)
	sh_material.set_shader_parameter("phase", _phase)
	sh_material.set_shader_parameter("iso_scale_y", Iso.SCALE_Y)

#endregion

#region OVERRIDE_PRIVATE

## Функция процессинга, где [param delta] - время между кадрами:
## сведение радиуса и частоты к цели, продвижение фазы.
func _process(delta: float) -> void:
	_radius = move_toward(_radius, _target_radius, RADIUS_SPEED * delta)
	_pulse_speed = move_toward(_pulse_speed, _target_pulse_speed, PULSE_RATE * delta)
	_phase = fmod(_phase + _pulse_speed * delta, 1.0)
	_apply()

#endregion
#endregion
