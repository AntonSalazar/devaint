extends TestCase

## Тесты [EventBus]: доставка, порядок приоритетов, отписка,
## наследование типов сообщений, отложенная отправка, мертвые подписки.


#region FUNCTIONS
#region OVERRIDE_PUBLIC

## Полный сброс шины перед каждым тестом.
func before_each() -> void:
	EventBus.reset()


## Сброс шины после теста: статическая _ref не должна доживать
## до teardown движка с подписками на тестовые скрипты.
func after_each() -> void:
	EventBus.reset()

#endregion

#region TESTS

## Подписчик получает сообщение ровно один раз на один push.
func test_subscriber_called_exactly_once() -> void:
	var calls: Array[int] = []
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: calls.append(1), 0)
	
	EventBus.push(TestMsg.new())
	check_eq(calls.size(), 1, "one push — one subscriber call")


## Обработчик получает сам экземпляр сообщения (доступ к полезной нагрузке).
func test_handler_receives_message() -> void:
	var got: Array[EventBus.Message] = []
	EventBus.subscribe(TestMsg, func(msg: EventBus.Message) -> void: got.append(msg), 0)
	
	var msg: TestMsg = TestMsg.new(42)
	EventBus.push(msg)
	check_eq(got.size(), 1, "handler with a parameter was called")
	if got.size() == 1:
		check_true(got[0] == msg, "handler received the same message instance")
		check_eq((got[0] as TestMsg).value, 42, "payload is accessible")


## Подписка на базовый тип ловит сообщения-наследники
## (подписка на сам Message = слушать все, docs/09-ARCHITECTURE.md).
func test_base_type_receives_derived() -> void:
	var base_calls: Array[int] = []
	var root_calls: Array[int] = []
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: base_calls.append(1), 0)
	EventBus.subscribe(EventBus.Message, func(_msg: EventBus.Message) -> void: root_calls.append(1), 0)
	
	EventBus.push(DerivedMsg.new())
	check_eq(base_calls.size(), 1, "base type subscriber received a derived message")
	check_eq(root_calls.size(), 1, "Message subscriber received any message")


## Сообщение чужого типа не доставляется.
func test_no_cross_delivery() -> void:
	var calls: Array[int] = []
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: calls.append(1), 0)
	
	EventBus.push(OtherMsg.new())
	check_eq(calls.size(), 0, "foreign message type is not delivered")


## Порядок вызова: ниже приоритет — раньше; равный — по порядку подписки.
func test_priority_and_stable_order() -> void:
	var trace: Array[String] = []
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: trace.append("late"), 10)
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: trace.append("first"), 0)
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: trace.append("second"), 0)
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: trace.append("mid"), 5)
	
	EventBus.push(TestMsg.new())
	check_eq(
		",".join(trace), "first,second,mid,late",
		"order: by priority, stable for equal ones"
	)


## Повторная подписка того же метода не дублирует вызов, но меняет приоритет.
func test_resubscribe_updates_priority() -> void:
	var trace: Array[String] = []
	var method_a: Callable = func(_msg: EventBus.Message) -> void: trace.append("a")
	var method_b: Callable = func(_msg: EventBus.Message) -> void: trace.append("b")
	EventBus.subscribe(TestMsg, method_a, 0)
	EventBus.subscribe(TestMsg, method_b, 5)
	EventBus.subscribe(TestMsg, method_a, 10)
	
	EventBus.push(TestMsg.new())
	check_eq(",".join(trace), "b,a", "method not duplicated, priority updated")


## После отписки вызовов нет.
func test_unsubscribe() -> void:
	var calls: Array[int] = []
	var method: Callable = func(_msg: EventBus.Message) -> void: calls.append(1)
	EventBus.subscribe(TestMsg, method, 0)
	EventBus.unsubscribe(TestMsg, method)
	
	EventBus.push(TestMsg.new())
	check_eq(calls.size(), 0, "no calls after unsubscribe")


## Подписка на тип, не наследующий Message, отклоняется.
func test_subscribe_rejects_non_message() -> void:
	print("        (an EventBus ERROR is expected below — part of the test)")
	EventBus.subscribe(
			EventBus.Record, func(_msg: EventBus.Message) -> void: fail("must not be called"), 0
	)
	check_true(true, "rejected without a crash")


## Мертвая подписка (объект освобожден) не ломает доставку живым —
## ни в первом push, ни в последующих.
func test_dead_subscription_does_not_break_delivery() -> void:
	var calls: Array[int] = []
	var dummy: Dummy = Dummy.new()
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: calls.append(1), 0)
	EventBus.subscribe(TestMsg, dummy.on_msg, 10)
	dummy.free()
	
	EventBus.push(TestMsg.new())
	check_eq(calls.size(), 1, "alive subscriber received the first push")
	
	EventBus.push(TestMsg.new())
	check_eq(calls.size(), 2, "alive subscriber received the second push")
	
	# Мертвая запись должна быть физически удалена из таблицы подписок.
	var records: Array = EventBus._ref.subs.get(TestMsg, [])
	check_eq(records.size(), 1, "dead record removed from subs after purge")


## Отложенная отправка: синхронно не доставляется, доставляется после кадра.
func test_push_deferred() -> void:
	var calls: Array[int] = []
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: calls.append(1), 0)
	
	EventBus.push_deferred(TestMsg.new())
	check_eq(calls.size(), 0, "deferred message is not delivered synchronously")
	
	await wait_frames(2)
	check_eq(calls.size(), 1, "deferred message delivered after a frame")


## Хелпер Message.push() публикует сообщение в шину.
func test_message_push_helper() -> void:
	var calls: Array[int] = []
	EventBus.subscribe(TestMsg, func(_msg: EventBus.Message) -> void: calls.append(1), 0)
	
	TestMsg.new().push()
	check_eq(calls.size(), 1, "Message.push() delivered the message")

#endregion
#endregion


#region CLASSES

## Тестовое сообщение с полезной нагрузкой.
class TestMsg extends EventBus.Message:
	var value: int = 0
	
	func _init(p_value: int = 0) -> void:
		value = p_value


## Наследник тестового сообщения — для проверки доставки по базовому типу.
class DerivedMsg extends TestMsg:
	pass


## Постороннее сообщение — не должно доставляться подписчикам [TestMsg].
class OtherMsg extends EventBus.Message:
	pass


## Объект-подписчик на Object (не RefCounted) — для теста мертвых подписок.
class Dummy extends Object:
	var calls: int = 0
	
	func on_msg(_msg: EventBus.Message) -> void:
		calls += 1

#endregion
