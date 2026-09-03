extends TestCase

## Обертка C#-тестов: мост [CsTestBridge] рефлексией гоняет наследников
## CsTestCase (EventBus, GameClock и далее) и печатает построчный отчет,
## сюда вливается общий итог для раннера.


#region CONSTANTS

## Путь к скрипту-мосту C#-тестов.
const BRIDGE_PATH: String = "res://tests/CsTestBridge.cs"

#endregion


#region FUNCTIONS
#region TESTS

## Прогон всех C#-наборов одним методом (детали печатает мост).
func test_csharp_suites() -> void:
	var bridge: RefCounted = load(BRIDGE_PATH).new()
	var result: Dictionary = bridge.RunAll()
	
	checks += int(result["checks"])
	for reason: String in result["failures"]:
		failures.append(reason)
	check_true(int(result["checks"]) > 0, "C# suites executed some checks")


## Отложенная отправка C#-шины: синхронно не доставляется,
## доставляется после кадра (кадры может ждать только GDScript-сторона).
func test_csharp_push_deferred() -> void:
	var bridge: RefCounted = load(BRIDGE_PATH).new()
	bridge.BeginDeferredProbe()
	check_eq(bridge.DeferredCallCount(), 0, "deferred message is not delivered synchronously")
	
	await wait_frames(2)
	check_eq(bridge.DeferredCallCount(), 1, "deferred message delivered after a frame")
	bridge.EndDeferredProbe()

#endregion
#endregion
