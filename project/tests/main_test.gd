extends TestCase

## Тесты [Main]: запуск цикла, кламп кадровой дельты, ввод скорости,
## teardown с очисткой шины, повторный teardown.

#region CONSTANTS

## Сцена корня игры.
const MAIN_SCENE: String = "res://core/main/main.tscn"

#endregion


#region FUNCTIONS
#region OVERRIDE_PUBLIC

## Полный сброс шины перед каждым тестом.
func before_each() -> void:
	EventBus.reset()


## Сброс шины после теста.
func after_each() -> void:
	EventBus.reset()

#endregion

#region TESTS

## Запуск: часы созданы, стартовое время из START_MINUTES, оверлей подписан.
func test_launch() -> void:
	var main: Main = _spawn_main()
	
	check_true(main.get_clock() != null, "clock is created on launch")
	check_eq(
			main.get_clock().get_datetime_str(), "Day 1 08:00",
			"start time comes from START_MINUTES"
	)
	check_true(not EventBus._ref.subs.is_empty(), "overlay is subscribed to the bus")
	main.free()


## Кламп дельты: гигантский кадр впрыскивает не больше MAX_FRAME_DELTA.
func test_frame_delta_clamp() -> void:
	var main: Main = _spawn_main()
	var clock: GameClock = main.get_clock()
	
	main._process(100.0)
	check_eq(clock.get_datetime_str(), "Day 1 08:00", "one huge frame adds no full minute")
	
	var injected: float = Main.MAX_FRAME_DELTA
	while injected < GameClock.MINUTE_DURATION:
		main._process(100.0)
		injected += Main.MAX_FRAME_DELTA
	check_eq(
			clock.get_datetime_str(), "Day 1 08:01",
			"clamped frames accumulate into exactly one minute"
	)
	main.free()


## Ввод: пауза и смена скорости через input-действия.
func test_time_input_actions() -> void:
	var main: Main = _spawn_main()
	var clock: GameClock = main.get_clock()
	
	main._unhandled_input(_action("time_pause"))
	check_eq(clock.get_speed(), 0, "time_pause sets speed to x0")
	main._unhandled_input(_action("time_speed_up"))
	check_eq(clock.get_speed(), 1, "time_speed_up resumes to x1")
	main._unhandled_input(_action("time_speed_up"))
	check_eq(clock.get_speed(), 2, "time_speed_up raises to x2")
	main._unhandled_input(_action("time_speed_down"))
	check_eq(clock.get_speed(), 1, "time_speed_down lowers back to x1")
	main.free()


## Teardown: выход из дерева чистит шину и отпускает часы.
func test_teardown_resets_bus() -> void:
	var main: Main = _spawn_main()
	
	check_true(not EventBus._ref.subs.is_empty(), "bus has subscriptions while running")
	main.free()
	check_true(EventBus._ref.subs.is_empty(), "bus is clean after teardown")


## Повторный teardown (WM_CLOSE_REQUEST + выход из дерева) безопасен.
func test_double_teardown_is_safe() -> void:
	var main: Main = _spawn_main()
	
	main.notification(Node.NOTIFICATION_WM_CLOSE_REQUEST)
	check_true(main.get_clock() == null, "close request releases the clock")
	check_true(EventBus._ref.subs.is_empty(), "close request cleans the bus")
	main.free()
	check_true(true, "second teardown on exit does not break")

#endregion

#region REGULAR_PRIVATE

## Создание [Main] из сцены с добавлением в корень дерева.
func _spawn_main() -> Main:
	var scene: PackedScene = load(MAIN_SCENE)
	var main: Main = scene.instantiate()
	var tree: SceneTree = Engine.get_main_loop() as SceneTree
	tree.root.add_child(main)
	return main


## Событие input-действия [param action_name] в нажатом состоянии.
func _action(action_name: String) -> InputEventAction:
	var event: InputEventAction = InputEventAction.new()
	event.action = action_name
	event.pressed = true
	return event

#endregion
#endregion
