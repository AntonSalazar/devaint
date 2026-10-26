-- Радар зомби: раз в секунду считает зомби рядом с каждым игроком и присылает число в его панель.
-- Кнопка панели меняет дальность по кругу.

local RANGES = { 10, 20, 40 }
local EVERY = 10

local range_of = {}
local ticks = 0

-- Дальность игрока, по умолчанию первая из списка
local function range(player)
  return RANGES[range_of[player] or 1]
end

-- Сколько зомби на этаже игрока в его дальности
local function count(player)
  local n = 0
  for _, entity in ipairs(entities.near(player.x, player.y, player.z, range(player.id))) do
    if entity.creature == "zombies:zombie" then
      n = n + 1
    end
  end
  return n
end

local function update()
  for _, player in ipairs(entities.players()) do
    ui.set(player.id, "zombies:nearby", count(player))
    ui.set(player.id, "zombies:range", range(player.id))
  end
end

hooks.add("tick", function()
  ticks = ticks + 1
  if ticks % EVERY == 1 then
    update()
  end
end)

hooks.add("ui_action", function(entity, action)
  if action == "zombies:radar_range" then
    range_of[entity] = (range_of[entity] or 1) % #RANGES + 1
    update()
  end
end)
