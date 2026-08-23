class_name Patrol
extends Node2D

## Класс патруля роя: активная сцена, следующая расписанию [PatrolRecord].
##
## Позицию не симулирует - ставит себя в [method PatrolRecord.sample] каждый кадр.
## На стоянках водит конусом восприятия по кругу ("сканирует").


#region CONSTANTS

## Длина конуса восприятия, наземные px.
const CONE_LENGTH: float = 300.0

## Полу-угол конуса, радин (~35 градусов).
const CONE_HALF_ANGLE: float = 0.6

## Скорость обзора настоянке, радиан за игровую минуту.
const SCAN_RATE: float = TAU / 2.0

#endregion


#region VARIABLES
#region REGULAR_PRIVATE

## Ссылка на экземпляр таймера игрового времени.
var _clock: GameClock = null

## Ссылка на экземпляр записи расписания патрулирования.
var _record: PatrolRecord = null

#endregion
#region ONREADY_PRIVATE

## Ссылка на экземпляр пивота конуса.
@onready var _cone_pivot: Node2D = %ConePivot

## Ссылка на экземляр взгляда.
@onready var _cone: Polygon2D = %Cone

#endregion
#endregion


#region FUNCTIONS
#region REGULAR_PUBLIC

## Функция инициализации.
func init(clock: GameClock, record: PatrolRecord) -> void:
	_clock = clock
	_record = record
	set_process(true)


## Функция деинициализации.
func deinit() -> void:
	set_process(false)
	_clock = null
	_record = null

#endregion

#region REGULAR_PRIVATE

## Функция наземного угла экранного направления [param heading].
## Изометрия разжимается, потому что пивот конуса сжат по Y.
func _ground_angle(heading: Vector2) -> float:
	return Vector2(heading.x, heading.y / Iso.SCALE_Y).angle()

#endregion

#region OVERRIDE_PRIVATE

## Функция, вызываемая при первом кадре.
func _ready() -> void:
	set_process(false)
	
	# Рисуем веер.
	var points: PackedVector2Array = [Vector2.ZERO]
	for idx: int in 13:
		var angle: float = -CONE_HALF_ANGLE + CONE_HALF_ANGLE * 2.0 * idx / 12.0
		points.append(Vector2.from_angle(angle) * CONE_LENGTH)
	_cone.polygon = points


## Функция процессинга, где [param delta] - время между кадрами.
## Вычисляет позицию и взгляд из расписания.
@warning_ignore("unused_parameter")
func _process(delta: float) -> void:
	var time: float = _clock.get_time_minutes()
	var sample: PatrolRecord.Sample = _record.sample(time)
	global_position = sample.position
	
	# Взгляд: на ходу - по курсу, на стоянке - круговой обзор.
	if sample.dwelling:
		_cone_pivot.rotation = fposmod(time * SCAN_RATE, TAU)
	else:
		_cone_pivot.rotation = _ground_angle(sample.heading)

#endregion
#endregion
