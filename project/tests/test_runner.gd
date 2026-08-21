extends SceneTree

## Раннер тестов. Запуск: `just test`
## (godot --headless --path project -s res://tests/test_runner.gd).
## Находит в `res://tests/` скрипты `*_test.gd` (наследники [TestCase]),
## исполняет их методы `test_*`, печатает отчет.
## Код выхода: 0 — все прошло, 1 — есть провалы.

#region CONSTANTS

## Каталог с тестовыми скриптами.
const TESTS_DIR: String = "res://tests/"

#endregion


#region FUNCTIONS
#region OVERRIDE_PRIVATE

func _initialize() -> void:
	_run_all()

#endregion

#region REGULAR_PRIVATE

## Функция прогона всех тестов с отчетом и выходом.
func _run_all() -> void:
	var total: int = 0
	var failed: int = 0
	
	for path: String in _find_test_scripts():
		print("\n=== %s ===" % path.get_file())
		
		# Скрипт обязан быть наследником TestCase.
		var script: GDScript = load(path)
		var case: TestCase = script.new() as TestCase
		if case == null:
			push_error("TestRunner: `%s` does not inherit TestCase, skipped" % path)
			failed += 1
			continue
		
		# Прогоняем тестовые методы по одному.
		for method_name: String in _find_test_methods(case):
			case.failures.clear()
			case.checks = 0
			
			case.before_each()
			await case.call(method_name)
			case.after_each()
			
			total += 1
			if case.failures.is_empty():
				print("  PASS  %s (%d checks)" % [method_name, case.checks])
			else:
				failed += 1
				print("  FAIL  %s" % method_name)
				for reason: String in case.failures:
					print("        - %s" % reason)
	
	# Итог.
	print("\n=== TOTAL: %d tests, %d failed ===" % [total, failed])
	quit(1 if failed > 0 else 0)


## Функция поиска тестовых скриптов в [constant TESTS_DIR].
func _find_test_scripts() -> Array[String]:
	var paths: Array[String] = []
	for file: String in DirAccess.get_files_at(TESTS_DIR):
		if file.ends_with("_test.gd"):
			paths.append(TESTS_DIR + file)
	paths.sort()
	return paths


## Функция поиска методов `test_*` у экземпляра [param case]
## с сохранением порядка объявления.
func _find_test_methods(case: TestCase) -> Array[String]:
	var names: Array[String] = []
	for method: Dictionary in case.get_method_list():
		var method_name: String = method["name"]
		if method_name.begins_with("test_") and not names.has(method_name):
			names.append(method_name)
	return names

#endregion
#endregion
