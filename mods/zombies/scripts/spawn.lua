-- Точка появления как в Project Zomboid: там, где игрок появился, зомби быть не должно.
-- Берётся первая по списку точка без зомби ближе SAFE метров, а если таких нет - самая далёкая от зомби.

local SAFE = 4
local SEARCH = 20

-- Расстояние от центра клетки точки до ближайшего зомби, SEARCH если зомби дальше
local function nearest_zombie(point)
  local cx, cy = point.x + 0.5, point.y + 0.5
  local nearest = SEARCH
  for _, entity in ipairs(entities.near(cx, cy, point.z, SEARCH)) do
    if entity.creature == "zombies:zombie" then
      local distance = math.sqrt((entity.x - cx) ^ 2 + (entity.y - cy) ^ 2)
      if distance < nearest then
        nearest = distance
      end
    end
  end
  return nearest
end

hooks.add("choose_spawn", function(player, points)
  local best, best_distance = nil, -1
  for i, point in ipairs(points) do
    local distance = nearest_zombie(point)
    if distance >= SAFE then
      return i
    end
    if distance > best_distance then
      best, best_distance = i, distance
    end
  end
  return best
end)
