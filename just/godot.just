# Root. `res://`
PROJECT_ROOT := "project"

# Engine
GODOT := env("GODOT", "godot4")

# Run
run:
    {{GODOT}} --path project/ --run

# Editor
editor:
    {{GODOT}} --path project/ --editor

# Autotests
test:
    {{GODOT}} --headless --path project -s res://tests/TestRunner.gd
