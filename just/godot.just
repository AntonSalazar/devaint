# Root. `res://`
PROJECT_ROOT := "project"

# Engine
GODOT := env("GODOT", "godot-mono")

# Build: собрать C#-сборку (нужно перед запуском, если менялся C#-код)
build:
    dotnet build project/

# Run
run: build
    {{GODOT}} --path project/ --run

# Editor
editor:
    {{GODOT}} --path project/ --editor

# Autotests: стиль, xUnit-тесты симуляции, Godot-тесты сцен
test: build test-sim
    dotnet format project/DevAInt.sln --verify-no-changes
    {{GODOT}} --headless --path project --import
    {{GODOT}} --headless --path project -s res://tests/test_runner.gd

# Sim tests: быстрый цикл без движка
test-sim:
    dotnet test sim/DevAInt.Sim.Tests/

# Format: поправить стиль C# по .editorconfig
format:
    dotnet format project/DevAInt.sln

# Screenshot dir (Movie Maker frames)
SHOT_DIR := env("SHOT_DIR", "/tmp/devaint-shots")

# Screenshot: записать ~2 сек кадров в SHOT_DIR, последний кадр — снимок
shot: build
    rm -rf {{SHOT_DIR}} && mkdir -p {{SHOT_DIR}}
    {{GODOT}} --path project --write-movie {{SHOT_DIR}}/frame.png --fixed-fps 10 --quit-after 20 --resolution 1280x720 > /dev/null 2>&1
    @ls {{SHOT_DIR}}/*.png | tail -1
