using System;
using System.Collections.Generic;

/// <summary>
/// Тесты <see cref="EventBus"/>: доставка, порядок приоритетов, отписка,
/// наследование типов сообщений, отписка во время прогона.
/// Мертвые подписки и отклонение чужих типов не тестируются:
/// в C# делегаты не умирают, а типы стережет компилятор.
/// </summary>
public class EventBusTest : CsTestCase
{
    /// <summary>Полный сброс шины перед каждым тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Сброс шины после теста: статическое состояние не должно утекать.</summary>
    public override void AfterEach() => EventBus.Reset();

    /// <summary>Подписчик получает сообщение ровно один раз на один Push.</summary>
    public void TestSubscriberCalledExactlyOnce()
    {
        List<int> calls = [];
        EventBus.Subscribe<TestMsg>(msg => calls.Add(1));

        EventBus.Push(new TestMsg());
        CheckEq(calls.Count, 1, "one push - one subscriber call");
    }

    /// <summary>Обработчик получает сам экземпляр сообщения (доступ к полезной нагрузке).</summary>
    public void TestHandlerReceivesMessage()
    {
        List<TestMsg> got = [];
        EventBus.Subscribe<TestMsg>(got.Add);

        TestMsg msg = new(42);
        EventBus.Push(msg);
        CheckEq(got.Count, 1, "handler with a parameter was called");
        if (got.Count == 1)
        {
            CheckTrue(ReferenceEquals(got[0], msg), "handler received the same message instance");
            CheckEq(got[0].Value, 42, "payload is accessible");
        }
    }

    /// <summary>
    /// Подписка на базовый тип ловит сообщения-наследники
    /// (подписка на сам Message = слушать все, docs/09-ARCHITECTURE.md).
    /// </summary>
    public void TestBaseTypeReceivesDerived()
    {
        List<int> baseCalls = [];
        List<int> rootCalls = [];
        EventBus.Subscribe<TestMsg>(msg => baseCalls.Add(1));
        EventBus.Subscribe<EventBus.Message>(msg => rootCalls.Add(1));

        EventBus.Push(new DerivedMsg());
        CheckEq(baseCalls.Count, 1, "base type subscriber received a derived message");
        CheckEq(rootCalls.Count, 1, "Message subscriber received any message");
    }

    /// <summary>Сообщение чужого типа не доставляется.</summary>
    public void TestNoCrossDelivery()
    {
        List<int> calls = [];
        EventBus.Subscribe<TestMsg>(msg => calls.Add(1));

        EventBus.Push(new OtherMsg());
        CheckEq(calls.Count, 0, "foreign message type is not delivered");
    }

    /// <summary>Порядок вызова: ниже приоритет - раньше; равный - по порядку подписки.</summary>
    public void TestPriorityAndStableOrder()
    {
        List<string> trace = [];
        EventBus.Subscribe<TestMsg>(msg => trace.Add("late"), 10);
        EventBus.Subscribe<TestMsg>(msg => trace.Add("first"));
        EventBus.Subscribe<TestMsg>(msg => trace.Add("second"));
        EventBus.Subscribe<TestMsg>(msg => trace.Add("mid"), 5);

        EventBus.Push(new TestMsg());
        CheckEq(
            string.Join(",", trace), "first,second,mid,late",
            "order: by priority, stable for equal ones");
    }

    /// <summary>Повторная подписка того же метода не дублирует вызов, но меняет приоритет.</summary>
    public void TestResubscribeUpdatesPriority()
    {
        List<string> trace = [];
        void methodA(TestMsg msg) => trace.Add("a");
        void methodB(TestMsg msg) => trace.Add("b");
        EventBus.Subscribe((Action<TestMsg>)methodA);
        EventBus.Subscribe((Action<TestMsg>)methodB, 5);
        EventBus.Subscribe((Action<TestMsg>)methodA, 10);

        EventBus.Push(new TestMsg());
        CheckEq(string.Join(",", trace), "b,a", "method not duplicated, priority updated");
    }

    /// <summary>После отписки вызовов нет.</summary>
    public void TestUnsubscribe()
    {
        List<int> calls = [];
        void method(TestMsg msg) => calls.Add(1);
        EventBus.Subscribe((Action<TestMsg>)method);
        EventBus.Unsubscribe((Action<TestMsg>)method);

        EventBus.Push(new TestMsg());
        CheckEq(calls.Count, 0, "no calls after unsubscribe");
    }

    /// <summary>Отписка чужой записи во время прогона гасит её до вызова.</summary>
    public void TestUnsubscribeDuringPush()
    {
        List<string> trace = [];
        void victim(TestMsg msg) => trace.Add("victim");
        EventBus.Subscribe<TestMsg>(msg => EventBus.Unsubscribe((Action<TestMsg>)victim));
        EventBus.Subscribe((Action<TestMsg>)victim, 10);

        EventBus.Push(new TestMsg());
        CheckEq(trace.Count, 0, "record unsubscribed mid-push is not invoked");
    }

    /// <summary>Хелпер Message.Push() публикует сообщение в шину.</summary>
    public void TestMessagePushHelper()
    {
        List<int> calls = [];
        EventBus.Subscribe<TestMsg>(msg => calls.Add(1));

        new TestMsg().Push();
        CheckEq(calls.Count, 1, "Message.Push() delivered the message");
    }

    /// <summary>Push возвращает тот же экземпляр сообщения с сохранением типа.</summary>
    public void TestPushReturnsSameInstance()
    {
        TestMsg msg = new(7);
        TestMsg returned = EventBus.Push(msg);
        CheckTrue(ReferenceEquals(returned, msg), "Push returns the same instance");
    }

    /// <summary>Тестовое сообщение с полезной нагрузкой.</summary>
    /// <param name="value">Полезная нагрузка.</param>
    public class TestMsg(int value = 0) : EventBus.Message
    {
        /// <summary>Полезная нагрузка.</summary>
        public int Value { get; } = value;
    }

    /// <summary>Наследник тестового сообщения - для проверки доставки по базовому типу.</summary>
    public class DerivedMsg : TestMsg;

    /// <summary>Постороннее сообщение - не должно доставляться подписчикам TestMsg.</summary>
    public class OtherMsg : EventBus.Message;
}
