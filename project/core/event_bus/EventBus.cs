using System;
using System.Collections.Generic;
using Godot;

public static class EventBus
{

    /// <summary>
    /// Таблица подписчиков, разбитых по ключам <see cref="Message"/> - типы сообщений
    /// и значениями <see cref="List{Record}"/>.
    /// </summary>
    private static readonly Dictionary<Type, List<Record>> _subs = [];

    /// <summary>
    /// Закэшированная таблица подписчиков, где порядок уже отсортирован для вызова.
    /// </summary>
    private static readonly Dictionary<Type, List<Record>> _cache = [];

    /// <summary>
    /// Список отложенных сообщений <see cref="Message"/> на конец кадра.
    /// </summary>
    private static readonly List<Message> _deferred = [];

    /// <summary>
    /// Глобальный порядковый номер подписки.
    /// </summary>
    private static int _orderCounter;


    /// <summary>
    /// Статичный метод сброса шины.
    /// </summary>
    public static void Reset()
    {
        _subs.Clear();
        _cache.Clear();
        _deferred.Clear();
        _orderCounter = 0;
    }


    /// <summary>
    /// Статичный метод подписки метода на выбранный тип сообщений по приоритету.
    /// </summary>
    /// <param name="method">Ссылка на метод, который будет подписан.</param>
    /// <param name="priority">Приоритет подписки. Выше - позже.</param>
    /// <typeparam name="T">Тип должен быть унаследован от <see cref="Message"/>.</typeparam>
    public static void Subscribe<T>(Action<T> method, int priority = 0) where T : Message
    {
        Type key = typeof(T);
        if (!_subs.TryGetValue(key, out List<Record>? records))
        {
            // Ключа не было, создадим список.
            records = [];
            _subs[key] = records;
        }

        // Пройдёмся по подписчикам. Если подписывается один и тот же метод,
        // то просто поменяем его порядок.
        foreach (Record record in records)
        {
            // Ищем тот же самый метод.
            if (record.Key != (Delegate)method)
            {
                continue;
            }

            // Если совпал приоритет - завершаем, ничего не поменялось.
            if (record.Priority == priority)
            {
                return;
            }

            // Найден уже подписанный метод, обновим порядок.
            record.Priority = priority;
            _cache.Clear();
            return;
        }

        // Добавляем нового подписчика, обновляя порядок.
        records.Add(new(method, msg => method((T)msg), priority, _orderCounter));
        _orderCounter++;
        _cache.Clear();
    }


    /// <summary>
    /// Статичный метод сброса подписки у метода.
    /// </summary>
    /// <param name="method">Ссылка на метод, который будет отписан.</param>
    /// <typeparam name="T">Тип должен быть унаследован от <see cref="Message"/>.</typeparam>
    public static void Unsubscribe<T>(Action<T> method) where T : Message
    {
        Type key = typeof(T);
        if (!_subs.TryGetValue(key, out List<Record>? records))
        {
            // Если списка нет, то просто будет взят пустой.
            return;
        }

        // Делаем прогон.
        for (int idx = 0; idx < records.Count; idx++)
        {
            // Ищем конкретный метод.
            Record record = records[idx];
            if (record.Key != (Delegate)method)
            {
                continue;
            }

            // Снимаем запись.
            record.Active = false;
            records.RemoveAt(idx);
            _cache.Remove(key);
            return;
        }
    }


    /// <summary>
    /// Статичный метод публикации сообщения.
    /// </summary>
    /// <param name="message">Ссылка на экземпляр сообщения, который будет выпущен.</param>
    /// <returns>
    /// Вернет то же самое сообщение, на случай, если в нём данные отредактированы.
    /// </returns>
    /// <typeparam name="T">Тип должен быть унаследован от <see cref="Message"/>.</typeparam>
    public static T Push<T>(T message) where T : Message
    {
        Type key = message.GetType();
        List<Record> records = GetRecords(key);
        foreach (Record record in records)
        {
            // Пропускаем неактивные.
            if (!record.Active)
            {
                continue;
            }

            // Делаем вызов.
            record.Invoke(message);
        }
        return message;
    }


    /// <summary>
    /// Статичный метод отложенной отправки сообщения на конец кадра.
    /// </summary>
    /// <param name="message">Ссылка на экземпляр сообщения, которое будет отправлено.</param>
    public static void PushDeferred(Message message)
    {
        if (_deferred.Count == 0)
        {
            Callable.From(PushDeferredList).CallDeferred();
        }
        _deferred.Add(message);
    }


    /// <summary>
    /// Статичный метод отправки всех накопленных сообщений на конец кадра.
    /// </summary>
    private static void PushDeferredList()
    {
        for (int idx = 0; idx < _deferred.Count; idx++)
        {
            Push(_deferred[idx]);
        }
        _deferred.Clear();
    }


    /// <summary>
    /// Статичный метод возврата списка записей к типу сообщений.
    /// </summary>
    /// <param name="messageType">Тип сообщения.</param>
    /// <returns>Вернёт список записей <see cref="List{Record}"/>.</returns>
    private static List<Record> GetRecords(Type messageType)
    {
        // Возьмем кэш, если он не пуст.
        if (_cache.TryGetValue(messageType, out List<Record>? cached))
        {
            return cached;
        }

        // Раз в кэше не нашлось, пройдемся по сырым подпискам.
        List<Record> records = [];
        for (
                Type? cursor = messageType;
                cursor is not null && cursor != typeof(object);
                cursor = cursor.BaseType)
        {
            if (_subs.TryGetValue(cursor, out List<Record>? subs))
            {
                records.AddRange(subs);
            }
        }

        // Делаем сортировку по приоритетам.
        records.Sort((a, b) => a.Priority != b.Priority
                ? a.Priority.CompareTo(b.Priority)
                : a.Order.CompareTo(b.Order));

        // Кэшируем и вернем результат.
        _cache[messageType] = records;
        return records;
    }


    /// <summary>Абстрактный класс сообщения.</summary>
    public abstract class Message
    {
        /// <summary>
        /// Метод публикации текущего экземпляра сообщения.
        /// </summary>
        /// <returns>
        /// Вернет этот же самый экземпляр на случай,
        /// если в сообщении данные модифицированы.
        /// </returns>
        public Message Push() => EventBus.Push(this);
    }


    /// <summary>
    /// Класс записи о подписчике.
    /// </summary>
    /// <param name="key">Ключ в виде ссылки на делегат для подписки/отписки.</param>
    /// <param name="invoke">Ссылка на экшон методов, которые будут вызваны.</param>
    /// <param name="priority">Номер приоритета. Выше - позже.</param>
    /// <param name="order">Номер записи. Нужен для стабильности порядка вызова при равном приоритете.</param>
    private class Record(Delegate key, Action<Message> invoke, int priority, int order)
    {
        /// <summary>
        /// Ключ в виде ссылки на делегат для подписки/отписки.
        /// </summary>
        public Delegate Key { get; } = key;

        /// <summary>
        /// Ссылка на экшон методов, которые будут вызваны.
        /// </summary>
        public Action<Message> Invoke { get; } = invoke;

        /// <summary>
        /// Номер приоритета. Выше - позже.
        /// </summary>
        public int Priority { get; set; } = priority;

        /// <summary>
        /// Номер записи. Нужен для стабильности порядка вызова при равном приоритете.
        /// </summary>
        public int Order { get; } = order;

        /// <summary>
        /// Флаг работы записи. По-умолчанию true, но гасится при отписке.
        /// </summary>
        public bool Active { get; set; } = true;
    }
}

