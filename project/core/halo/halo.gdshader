shader_type canvas_item;

// Цвет игрока (бирюза, 06-VISUAL-UI.md).
uniform vec4 halo_color : source_color = vec4(0.3, 0.9, 0.85, 1.0);
// Радиус по горизонтали, px; фаза пульса [0, 1); толщина кольца в долях радиуса.
uniform float radius = 90.0;
uniform float phase = 0.0;
uniform float ring_width = 0.12;
uniform float iso_scale_y = 0.5;

varying vec2 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	// Расстояние по земле: эллипс 2:1 на экране.
	vec2 ground = vec2(local_pos.x, local_pos.y / iso_scale_y);
	float d = length(ground) / radius;
	// Бегущее от центра кольцо, гаснущее к краю. Фаза приходит из скрипта.
	float ring = 1.0 - smoothstep(0.0, ring_width, abs(d - phase));
	float fade = 1.0 - phase;
	// Постоянное тонкое кольцо на границе: радиус следа читается без пульса.
	float edge = 1.0 - smoothstep(0.0, 0.04, abs(d - 1.0));
	// Тихая подложка-диск, чтобы ореол читался и между пульсами.
	float disk = (1.0 - smoothstep(0.0, 1.0, d)) * 0.3;
	float alpha = (d > 1.0) ? 0.0 : disk + ring * fade * 0.8 + edge * 0.5;
	COLOR = vec4(halo_color.rgb, min(alpha, 1.0));
}
