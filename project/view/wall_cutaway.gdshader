shader_type spatial;

// Внутренние грани рисуются, чтобы опущенный кусок изнутри выглядел сплошным, а не пустой коробкой.
render_mode cull_disabled;

// Срез стен как в Project Zomboid: кусок стены опускается целиком до пенька высотой cut_height.
// Какие куски опустить, решает CutawayPlan на процессоре по комнатам и перекрытию персонажа,
// и пишет признак в красный канал своих данных экземпляра MultiMesh.
// Тени этот шейдер не отбрасывает: целые стены для теней рисует отдельный узел.

// Высота пенька, который остаётся от опущенной стены.
uniform float cut_height = 0.3;

varying vec3 world_position;
varying float cut;
varying float base_y;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	cut = INSTANCE_CUSTOM.r;

	// У MultiMesh матрица модели включает положение экземпляра: начало - центр куска, ось Y - высота
	base_y = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).y - length(MODEL_MATRIX[1].xyz) * 0.5;
}

void fragment() {
	ALBEDO = COLOR.rgb;
	if (!FRONT_FACING) {
		// Изнутри видно только там, где кусок опущен: освещаем как верх стены
		NORMAL = (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
	}
	if (cut > 0.5 && world_position.y - base_y > cut_height) {
		discard;
	}
}
