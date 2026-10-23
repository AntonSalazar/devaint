shader_type spatial;

// Внутренние грани рисуются, чтобы срезанный кусок изнутри выглядел сплошным, а не пустой коробкой.
render_mode cull_disabled;

// Срез стен перед персонажем кругом на экране. Кусок стены срезается, только если персонаж и камера
// стоят по разные стороны от него: тогда стена между ними и может закрывать персонажа. Стена за
// спиной или сбоку, на одной стороне с камерой, не режется никогда, даже вплотную. У срезаемого
// куска не рисуются пиксели в круге radius вокруг центра тела на экране выше cut_height над полом.
// Тени этот шейдер не отбрасывает: целые стены для теней рисует отдельный узел.

// Центр тела персонажа в мире.
uniform vec3 focus = vec3(0.0);

// Высота пола под персонажем.
uniform float floor_y = 0.0;

// Радиус круга среза на экране, в метрах: от центра тела хватает на ноги и немного пола вокруг.
uniform float radius = 1.8;

// Высота пенька, который остаётся от срезанной стены.
uniform float cut_height = 0.3;

// Насколько персонаж должен отойти от линии стены, чтобы считаться стоящим по одну из её сторон:
// в дверном проёме он стоит на самой линии, и срез не должен мигать.
uniform float side_epsilon = 0.05;

// Срез включён, пока у вида есть свой персонаж.
uniform bool enabled = false;

varying vec3 world_position;
varying float between;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	between = 0.0;
	if (enabled) {
		// У MultiMesh матрица модели включает положение экземпляра: начало - центр куска стены,
		// ось Z - толщина стены, то есть нормаль к её линии по земле
		vec2 center = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xz;
		vec2 normal = normalize(MODEL_MATRIX[2].xz);
		float player_side = dot(focus.xz - center, normal);
		float camera_side = dot(INV_VIEW_MATRIX[2].xz, normal);
		if (abs(player_side) > side_epsilon && sign(player_side) != sign(camera_side)) {
			between = 1.0;
		}
	}
}

void fragment() {
	ALBEDO = COLOR.rgb;
	if (!FRONT_FACING) {
		// Изнутри видно только там, где кусок срезан: освещаем как верх стены
		NORMAL = (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
	}
	if (between > 0.5 && world_position.y - floor_y > cut_height) {
		// У ортографической камеры расстояние поперёк взгляда - это расстояние на экране
		vec3 back = INV_VIEW_MATRIX[2].xyz;
		vec3 offset = world_position - focus;
		vec3 on_screen = offset - back * dot(offset, back);
		if (length(on_screen) < radius) {
			discard;
		}
	}
}
