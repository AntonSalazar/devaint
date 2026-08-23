shader_type canvas_item;

// Картинка статики SignalGrid: R = покрытие клетки.
uniform sampler2D coverage_tex : filter_nearest, repeat_disable;
// Геометрия грида и тайла (из Iso).
uniform vec2 grid_origin;
uniform vec2 grid_size;
uniform vec2 tile_size;
// Цвет поля роя и его максимальная непрозрачность.
uniform vec4 field_color : source_color = vec4(1.0, 0.2, 0.15, 1.0);
uniform float max_alpha = 0.55;

varying vec2 world_pos;

void vertex() {
      world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
}

void fragment() {
      // Мир -> клетка: та же формула, что Iso.world_to_cell, округление к ближайшей.
      float cx = world_pos.x / tile_size.x + world_pos.y / tile_size.y - 1.0;
      float cy = world_pos.y / tile_size.y - world_pos.x / tile_size.x;
      vec2 cell = vec2(round(cx), round(cy));
      vec2 local = cell - grid_origin;
      float coverage = 0.0;
      if (local.x >= 0.0 && local.y >= 0.0 && local.x < grid_size.x && local.y < grid_size.y) {
              coverage = texture(coverage_tex, (local + 0.5) / grid_size).r;
      }
      COLOR = vec4(field_color.rgb, coverage * max_alpha);
}