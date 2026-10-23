# Root. `res://`
PROJECT_ROOT := "project"

# Engine
GODOT := env("GODOT", "godot-mono")

# Build: собрать все C#-сборки
build:
    dotnet build {{PROJECT_ROOT}}/DevAInt.sln

# Run: запустить игру
run: build
    {{GODOT}} --path {{PROJECT_ROOT}}

# Editor
editor:
    {{GODOT}} --path {{PROJECT_ROOT}} --editor

# Server: headless-запуск, пока только пустая сцена, настоящий сервер на этапе 10
server: build
    {{GODOT}} --headless --path {{PROJECT_ROOT}} -- --server

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

# Format: поправить стиль C# по .editorconfig
format:
    dotnet format {{PROJECT_ROOT}}/DevAInt.sln

# Screenshot dir, кадры Movie Maker
SHOT_DIR := env("SHOT_DIR", "/tmp/devaint-shots")

# Screenshot: записать ~2 сек кадров в SHOT_DIR, последний кадр - снимок
shot: build
    rm -rf {{SHOT_DIR}} && mkdir -p {{SHOT_DIR}}
    {{GODOT}} --path {{PROJECT_ROOT}} --write-movie {{SHOT_DIR}}/frame.png --fixed-fps 10 --quit-after 20 --resolution 1280x720 > /dev/null 2>&1
    @ls {{SHOT_DIR}}/*.png | tail -1
