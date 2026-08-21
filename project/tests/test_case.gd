class_name TestCase
extends RefCounted

## Базовый класс теста.
## Наследники объявляют методы `test_*` — раннер вызывает их по одному.
## Проверки — через `check_*`; провалы копятся, тест не прерывается.

#region VARIABLES
#region REGULAR_PUBLIC

## Провалы текущего тестового метода.
var failures: Array[String] = []

## Количество проверок, выполненных текущим тестовым методом.
var checks: int = 0

#endregion
#endregion


#region FUNCTIONS
#region VIRTUAL_PUBLIC

## Вызывается перед каждым тестовым методом.
func before_each() -> void:
	pass


## Вызывается после каждого тестового метода.
func after_each() -> void:
	pass

#endregion

#region REGULAR_PUBLIC

## Проверка истинности условия [param condition].
func check_true(condition: bool, what: String) -> void:
	checks += 1
	if not condition:
		failures.append(what)


## Проверка равенства [param got] ожидаемому [param expected].
func check_eq(got: Variant, expected: Variant, what: String) -> void:
	checks += 1
	if got != expected:
		failures.append("%s: expected `%s`, got `%s`" % [what, expected, got])


## Проверка близости float [param got] к [param expected]
## с допуском [param tolerance].
func check_near(
		got: float, expected: float, what: String, tolerance: float = 0.00001
) -> void:
	checks += 1
	if absf(got - expected) > tolerance:
		failures.append("%s: expected ~`%s`, got `%s`" % [what, expected, got])


## Безусловный провал.
func fail(what: String) -> void:
	checks += 1
	failures.append(what)


## Ожидание [param count] кадров главного цикла (для отложенных вызовов).
## Вызывать строго через await.
func wait_frames(count: int = 1) -> void:
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	for _i: int in count:
		await tree.process_frame

#endregion
#endregion
