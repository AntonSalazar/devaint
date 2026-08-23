# Root. `res://`
PROJECT_ROOT := "project"

# Engine
GODOT := env("GODOT", "godot_last")

# Run
run:
    {{GODOT}} --path project/ --run

# Editor
editor:
    {{GODOT}} --path project/ --editor

# Autotests
test:
    {{GODOT}} --headless --path project --import
    {{GODOT}} --headless --path project -s res://tests/test_runner.gd

# Screenshot dir (Movie Maker frames)
SHOT_DIR := env("SHOT_DIR", "/tmp/devaint-shots")

# Screenshot: записать ~2 сек кадров в SHOT_DIR, последний кадр — снимок
shot:
    rm -rf {{SHOT_DIR}} && mkdir -p {{SHOT_DIR}}
    {{GODOT}} --path project --write-movie {{SHOT_DIR}}/frame.png --fixed-fps 10 --quit-after 20 --resolution 1280x720 > /dev/null 2>&1
    @ls {{SHOT_DIR}}/*.png | tail -1
