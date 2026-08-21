class_name EventBus
extends RefCounted

## Статичный класс шины.

#region PROPERTIES
#region REF

## Синглтон ссылка.
static var _ref: EventBus = null:
	get():
		if not is_instance_valid(_ref):
			_ref = EventBus.new()
		return _ref

#endregion
#endregion


#region VARIABLES
#region REGULAR_PUBLIC

## Таблица подписчиков, разбитых по ключам [GDScript] - типы сообщений,
## и значениям [Array[EventBus.Record]].
var subs: Dictionary[GDScript, Array] = {}

## Закэшированная таблица подписчиков, чей порядок уже отсортирован для вызовов.
var cache: Dictionary[GDScript, Array] = {}

## Список отложенных сообщений на конец кадра.
var deferred: Array[Message] = []

## Глобальный порядковый номер подписки.
var order_counter: int = 0

#endregion
#endregion


#region FUNCTIONS
#region STATIC_PUBLIC

## Статичная функция подписки метода [param method]
## на тип сообщений [param message_t] по приоритету [param priority] (выше - позже).
static func subscribe(message_t: GDScript, method: Callable, priority: int) -> void:
	# Проверяем тип.
	if not _is_message_type(message_t):
		push_error(
				"EventBus.subscribe: `message_t` must inherit `Message`, got: %s" %
				message_t
		)
		return
	
	# Проверяем, что метод доступен.
	if not method.is_valid():
		push_error("EventBus.subscribe: invalid `method` detected")
		return
	
	# Пройдемся по подписчикам. Если подписывается один и тот же метод,
	# то нужно поменять лишь его порядок.
	@warning_ignore("shadowed_variable")
	var records: Array[Record] = _ref.subs.get_or_add(message_t, _empty())
	for record: Record in records:
		# Ищем тот же самый метод.
		if record.method != method:
			continue
		
		# Если совпал приоритет - завершаем - ничего не поменялось.
		if record.priority == priority:
			return
		
		# Найден уже подписанный метод, обновим порядок
		record.priority = priority
		_ref.cache.clear()
		return
	
	# Добавляем нового подписчика, обновляем порядок.
	records.append(Record.new(method, priority, _ref.order_counter))
	_ref.order_counter += 1
	_ref.cache.clear()


## Статичная функция снятия подписки метода [param method]
## на тип сообщений [param message_t].
static func unsubscribe(message_t: GDScript, method: Callable) -> void:
	var records: Array[Record] = _ref.subs.get(message_t, _empty())
	for idx: int in records.size():
		var record: Record = records[idx]
		if record.method != method:
			continue
		
		# Снимаем запись.
		record.deinit()
		records.remove_at(idx)
		_ref.cache.erase(message_t)
		return


## Статичная функция публикации сообщения [param message] в шину.
static func push(message: Message) -> void:
	# Флаг для определения мертвых подписок.
	# В конце прогона они будут удалены.
	var dead: bool = false
	
	# Делаем прогон.
	var message_t: GDScript = message.get_script()
	var records := _get_records(message_t)
	for record: Record in records:
		# Пропускаем неактивные.
		if not record.active:
			continue
		
		# Помечаем мертвые методы.
		if not record.method.is_valid():
			record.deinit()
			dead = true
			continue
		
		# Делаем вызов.
		record.method.call(message)
	
	# Если были мертвые подписки, то самое время их стереть.
	if dead:
		_purge_dead()


## Статичная функция отложенной отправки сообщения [param message] на конец кадра.
static func push_deferred(message: Message) -> void:
	var messages := _ref.deferred
	if messages.is_empty():
		_ref._push_deferred.call_deferred()
	messages.append(message)


## Статичная функция сброса шины.
static func reset() -> void:
	_ref = null

#endregion

#region STATIC_PRIVATE

## Статичная функция проверяет, что тип - наследник Message.
static func _is_message_type(message_t: GDScript) -> bool:
	var cursor: GDScript = message_t
	while is_instance_valid(cursor):
		if cursor == Message:
			return true
		cursor = cursor.get_base_script()
	return false


## Статичная функция возврата пустого типизированного массива записей [Record].
static func _empty() -> Array[Record]:
	var records: Array[Record] = Array([], TYPE_OBJECT, "RefCounted", Record)
	return records


## Статичная функция возврата списка записей к типу сообщений [param message_t].
static func _get_records(message_t: GDScript) -> Array[Record]:
	# Возьмем кэш, если он не пуст.
	var records: Array[Record] = _ref.cache.get(message_t, _empty())
	if records.size() > 0:
		return records
	
	# Раз он пуст, то возьмем сырые подписки
	var cursor: GDScript = message_t
	while is_instance_valid(cursor):
		records.append_array(_ref.subs.get(cursor, _empty()))
		cursor = cursor.get_base_script()
	
	# Проводим сортировку по приоритетам.
	records.sort_custom(
		func(a: Record, b: Record) -> bool:
			if a.priority != b.priority:
				return a.priority < b.priority
			return a.order < b.order
	)
	
	# Кэшируем.
	_ref.cache[message_t] = records
	return records


## Статичная функция удаления мертвых подписок.
static func _purge_dead() -> void:
	# Пройдемся по отключенным записям.
	var all_records: Array = _ref.subs.values()
	for idx: int in all_records.size():
		var records: Array[Record] = all_records[idx]
		for rec_idx: int in range(records.size() - 1, -1, -1):
			var record: Record = records[rec_idx]
			if record.active:
				continue
			
			# Удаляем запись.
			records.remove_at(rec_idx)
	
	# Сбросим кэш.
	_ref.cache.clear()

#endregion

#region REGULAR_PRIVATE

## Функция осуществления отложенной отправки с очисткой очереди.
func _push_deferred() -> void:
	# Делаем отправку.
	for message: Message in deferred:
		push(message)
	
	# Очищаем очередь.
	deferred.clear()

#endregion
#endregion


#region CLASSES

## Класс записи о подписчике.
class Record extends RefCounted:
	#region VARIABLES
	#region REGULAR_PUBLIC
	
	## Ссылка на метод, который будет вызван.
	var method: Callable = Callable()
	
	## Номер приоритета. Выше - позже.
	var priority: int = 0
	
	## Номер записи. Нужен для стабильности порядка вызова при равном приоритете.
	var order: int = 0
	
	## Флаг активности подписки. Обновляется при создании и удалении записи.
	var active: bool = false
	
	#endregion
	#endregion
	
	
	#region FUNCTIONS
	#region REGULAR_PUBLIC
	
	## Функция деинициализации записи.
	func deinit() -> void:
		method = Callable()
		order = 0
		priority = 0
		active = false
	
	#endregion
	#region OVERRIDE_PRIVATE
	
	## Функция инициализации записи.
	func _init(p_method: Callable, p_priority: int, p_order: int) -> void:
		method = p_method
		priority = p_priority
		order = p_order
		active = true
	
	#endregion
	#endregion


## Абстрактный класс сообщения.
@abstract class Message extends RefCounted:
	#region FUNCTIONS
	#region REGULAR_PUBLIC
	
	## Функция публикации сообщения.
	func push() -> void:
		EventBus.push(self)
	
	#endregion
	
	#region OVERRIDE_PRIVATE
	
	## Функция возврата представления экземпляра класса в виде строки.
	func _to_string() -> String:
		return get_script().get_global_name()
	
	#endregion
	#endregion

#endregion
