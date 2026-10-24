# Root. `res://`
PROJECT_ROOT := "project"

# Engine
GODOT := env("GODOT", "godot-mono")

# Build: собрать все C#-сборки
build:
    dotnet build {{PROJECT_ROOT}}/DevAInt.sln

# Run: запустить игру; аргументы игры после имени: `just run --save test`, `just run --connect 127.0.0.1 --player bob --lag 100`
run *args: build
    {{GODOT}} --path {{PROJECT_ROOT}} -- {{args}}

# Editor
editor:
    {{GODOT}} --path {{PROJECT_ROOT}} --editor

# Server: выделенный сервер без окна, мир в saves/; аргументы после имени: `just server --port 7777 --save world`; Ctrl+C сохраняет и выходит
server *args: build
    {{GODOT}} --headless --path {{PROJECT_ROOT}} res://server/Server.tscn -- {{args}}

# Autotests: стиль, xUnit-тесты sim, тесты Godot-части
test: build test-sim
    dotnet format {{PROJECT_ROOT}}/DevAInt.sln --verify-no-changes
    {{GODOT}} --headless --path {{PROJECT_ROOT}} --import
    {{GODOT}} --headless --path {{PROJECT_ROOT}} res://tests/TestRunner.tscn

# Sim tests: быстрый цикл без движка
test-sim:
    dotnet test sim/DevAInt.Sim.Tests/

# Bench: замеры тика sim на 1000 сущностей, Release-сборка
bench:
    dotnet run -c Release --project sim/DevAInt.Sim.Bench/ -- mods

# Map: ASCII-дамп этажа карты из модов, без Godot: `just map core:starter_room 0`
map id="core:starter_room" z="0":
    dotnet run --project sim/DevAInt.Sim.Tool/ -- map mods {{id}} {{z}}

# Schema: JSON Schema файлов мода в schemas/ для редактора моддера, после правки типов определений
schema:
    dotnet run --project sim/DevAInt.Sim.Tool/ -- schema schemas

# Format: поправить стиль C# по .editorconfig
format:
    dotnet format {{PROJECT_ROOT}}/DevAInt.sln

# Screenshot dir, кадры Movie Maker
SHOT_DIR := env("SHOT_DIR", "/tmp/devaint-shots")

# Screenshot: ~3 сек кадров в SHOT_DIR, персонаж ходит по кругу; последний кадр - снимок
shot: build
    rm -rf {{SHOT_DIR}} && mkdir -p {{SHOT_DIR}}
    {{GODOT}} --path {{PROJECT_ROOT}} --write-movie {{SHOT_DIR}}/frame.png --fixed-fps 10 --quit-after 30 --resolution 1280x720 -- --autowalk > /dev/null 2>&1
    @ls {{SHOT_DIR}}/*.png | tail -1

# Server build: папка экспорта выделенного сервера
SERVER_DIR := "builds/server"

# Server host: устройство для тестового сервера, user@host из DEVAINT_SERVER_HOST в окружении или в .env
SERVER_HOST := env("DEVAINT_SERVER_HOST", "")

# Export server: собрать выделенный сервер Linux x86_64 и положить рядом моды
export-server: build
    rm -rf {{SERVER_DIR}} && mkdir -p {{SERVER_DIR}}
    {{GODOT}} --headless --path {{PROJECT_ROOT}} --export-release "Linux Server" ../{{SERVER_DIR}}/devaint-server.x86_64
    cp -r mods {{SERVER_DIR}}/mods

# Deploy server: собрать и скопировать сервер на SERVER_HOST в ~/devaint-server; старая сборка заменяется, saves/ остаётся
deploy-server: _need-server-host export-server
    tar -czf builds/devaint-server.tar.gz -C {{SERVER_DIR}} .
    scp builds/devaint-server.tar.gz {{SERVER_HOST}}:/tmp/devaint-server.tar.gz
    ssh {{SERVER_HOST}} 'mkdir -p ~/devaint-server && find ~/devaint-server -mindepth 1 -maxdepth 1 ! -name saves -exec rm -rf {} + && tar -xzf /tmp/devaint-server.tar.gz -C ~/devaint-server && rm /tmp/devaint-server.tar.gz'

# Remote server: запустить сервер на SERVER_HOST, лог здесь, Ctrl+C сохраняет мир и останавливает: `just remote-server --port 7777`
remote-server *args: _need-server-host
    ssh -t {{SERVER_HOST}} '~/devaint-server/devaint-server.x86_64 -- {{args}}'

# Проверка, что адрес устройства задан: в репозитории его нет
_need-server-host:
    @test -n "{{SERVER_HOST}}" || { echo "DEVAINT_SERVER_HOST is not set: export it or put DEVAINT_SERVER_HOST=user@host into .env" >&2; exit 1; }
