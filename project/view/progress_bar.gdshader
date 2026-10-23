shader_type spatial;

// Полоса прогресса над сущностью: плашка всегда смотрит в камеру и рисуется поверх мира,
// заливка слева направо до progress, остальное - тёмный фон.
render_mode unshaded, depth_test_disabled, cull_disabled, shadows_disabled;

// Доля выполнения от 0 до 1, своя у каждой сущности.
instance uniform float progress = 0.0;

// Цвет заливки.
uniform vec3 fill_color = vec3(0.95, 0.78, 0.3);

// Цвет фона.
uniform vec3 back_color = vec3(0.12, 0.12, 0.14);

void vertex() {
	// Поворот плашки к камере: оси камеры вместо осей узла, положение узла сохраняется
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}

void fragment() {
	ALBEDO = UV.x <= progress ? fill_color : back_color;
}
