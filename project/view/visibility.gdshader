shader_type spatial;

// Затемнение всего в кадре по видимости, как в Project Zomboid, но в 3D: для каждого пикселя по
// глубине восстанавливается точка мира, и шейдер сам проверяет, видят ли её глаза: точка в конусе
// взгляда или рядом, и луч от глаз до неё не пересекает стену. Так края тени идут ровно по стенам, а не
// по клеткам. Текстура стен - биты рёбер клетки, текстура памяти - видел ли персонаж точку раньше,
// 4 на 4 точки на клетку, чтобы граница разведанного тоже шла ровно.
// Видно сейчас или видел раньше - обычный цвет, чтобы не спорить с освещением, не видел никогда -
// сплошной цвет фона, как чернота в Project Zomboid: неизведанное сливается с пустотой за картой. Видимость сейчас нужна для мест, которые ещё не отмечены в памяти. Пиксели без
// глубины, фон за картой, не трогаются.
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, skip_vertex_transform, fog_disabled;

uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform sampler2D depth : hint_depth_texture, filter_nearest;
uniform sampler2D walls : filter_nearest, repeat_disable;
uniform sampler2D memory : filter_linear, repeat_disable;

// Клетка угла окна текстуры и сторона окна в клетках.
uniform ivec2 window_origin = ivec2(0);
uniform int window_cells = 128;

// Глаза и взгляд в мировых x и z, обзор: половина ширины конуса в радианах, дальность и радиус вокруг.
uniform vec2 eye = vec2(0.0);
uniform vec2 facing = vec2(0.0, 1.0);
uniform float half_angle = 3.14159;
uniform float range = 25.0;
uniform float near = 1.5;

// Цвет места, которое не видел никогда.
uniform vec3 unexplored : source_color = vec3(0.0);

// Ширина мягкого края конуса в радианах и краёв дальности и радиуса вокруг в метрах.
const float SOFT_ANGLE = 0.03;
const float SOFT_DISTANCE = 0.3;

// Биты стен клетки, как в VisibilityOverlay.
const int NORTH_WALL = 1;
const int WEST_WALL = 2;

void vertex() {
	// Квад 2 на 2 закрывает весь экран: вершины сразу в координатах экрана
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

// Загораживает ли взгляд ребро bit клетки cell; вне окна стен нет.
bool wall(ivec2 cell, int bit) {
	ivec2 texel = cell - window_origin;
	if (texel.x < 0 || texel.y < 0 || texel.x >= window_cells || texel.y >= window_cells) {
		return false;
	}

	int bits = int(texelFetch(walls, texel, 0).r * 255.0 + 0.5);
	return (bits & bit) != 0;
}

// Свободна ли прямая от глаз до точки: тот же проход по клеткам, что Sight.Clear в sim.
bool clear(vec2 to) {
	ivec2 cell = ivec2(floor(eye));
	ivec2 end = ivec2(floor(to));
	vec2 d = to - eye;
	ivec2 step_dir = ivec2(sign(d));
	vec2 delta = vec2(d.x == 0.0 ? 1e30 : 1.0 / abs(d.x), d.y == 0.0 ? 1e30 : 1.0 / abs(d.y));
	vec2 next = vec2(1e30);
	if (d.x != 0.0) {
		next.x = (step_dir.x > 0 ? float(cell.x + 1) - eye.x : eye.x - float(cell.x)) * delta.x;
	}

	if (d.y != 0.0) {
		next.y = (step_dir.y > 0 ? float(cell.y + 1) - eye.y : eye.y - float(cell.y)) * delta.y;
	}

	int steps = abs(end.x - cell.x) + abs(end.y - cell.y);
	for (int i = 0; i < 256; i++) {
		if (i >= steps) {
			break;
		}

		// Каждая клетка хранит северное и западное ребро: восточное и южное берутся у соседа
		if (next.x < next.y) {
			if (wall(step_dir.x > 0 ? cell + ivec2(1, 0) : cell, WEST_WALL)) {
				return false;
			}

			cell.x += step_dir.x;
			next.x += delta.x;
		} else {
			if (wall(step_dir.y > 0 ? cell + ivec2(0, 1) : cell, NORTH_WALL)) {
				return false;
			}

			cell.y += step_dir.y;
			next.y += delta.y;
		}
	}

	return true;
}

// Насколько точка в обзоре без учёта стен: 1 внутри, 0 снаружи, мягкий переход на краях.
float in_view(vec2 point) {
	vec2 d = point - eye;
	float distance = length(d);
	float inside_range = 1.0 - smoothstep(range - SOFT_DISTANCE, range, distance);
	float around = 1.0 - smoothstep(near - SOFT_DISTANCE, near, distance);
	float cone = 1.0;
	if (half_angle < 3.14159 && distance > 1e-4) {
		float angle = acos(clamp(dot(d / distance, facing), -1.0, 1.0));
		cone = 1.0 - smoothstep(half_angle - SOFT_ANGLE, half_angle + SOFT_ANGLE, angle);
	}

	return max(around, cone) * inside_range;
}

void fragment() {
	vec3 color = texture(screen, SCREEN_UV).rgb;
	float raw = texture(depth, SCREEN_UV).r;

	// Глубина обратная: дальняя плоскость - 0, там ничего не нарисовано
	if (raw <= 0.0) {
		ALBEDO = color;
	} else {
		vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
		view.xyz /= view.w;
		vec3 world = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
		float visible = in_view(world.xz);

		// Луч дорогой: только для точек, которые вообще в обзоре
		if (visible > 0.0 && !clear(world.xz)) {
			visible = 0.0;
		}

		float seen = texture(memory, (world.xz - vec2(window_origin)) / float(window_cells)).r;
		ALBEDO = mix(unexplored, color, max(seen, visible));
	}
}
