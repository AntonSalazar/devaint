using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Godot.Collections;

/// <summary>
/// Мост C#-тестов для GDScript-раннера: рефлексией находит наследников
/// <see cref="CsTestCase"/>, гоняет их методы Test*, печатает отчет
/// в формате раннера и возвращает итог обертке csharp_test.gd.
/// </summary>
public partial class CsTestBridge : RefCounted
{
    /// <summary>Вызовы, собранные пробой отложенной отправки.</summary>
    private readonly List<int> _deferredCalls = [];

    /// <summary>
    /// Прогон всех C#-тестов с построчным отчетом.
    /// </summary>
    /// <returns>Словарь с ключами `checks` (int) и `failures` (Array строк).</returns>
    public Dictionary RunAll()
    {
        int totalChecks = 0;
        Array<string> totalFailures = [];

        foreach (Type type in FindSuites())
        {
            CsTestCase suite = (CsTestCase)Activator.CreateInstance(type)!;
            foreach (MethodInfo method in FindTestMethods(type))
            {
                suite.Failures.Clear();
                suite.Checks = 0;

                suite.BeforeEach();
                try
                {
                    method.Invoke(suite, null);
                }
                catch (Exception error)
                {
                    suite.Fail($"exception: {Unwrap(error).Message}");
                }
                suite.AfterEach();

                // Ноль выполненных проверок - признак ошибки внутри теста.
                if (suite.Checks == 0)
                {
                    suite.Failures.Add("no checks executed (error inside the test?)");
                }

                totalChecks += suite.Checks;
                string title = $"{type.Name}.{method.Name}";
                if (suite.Failures.Count == 0)
                {
                    GD.Print($"  PASS  {title} ({suite.Checks} checks)");
                    continue;
                }

                GD.Print($"  FAIL  {title}");
                foreach (string reason in suite.Failures)
                {
                    GD.Print($"        - {reason}");
                    totalFailures.Add($"{title}: {reason}");
                }
            }
        }

        return new Dictionary
        {
            { "checks", totalChecks },
            { "failures", totalFailures },
        };
    }

    /// <summary>
    /// Начало пробы отложенной отправки: сброс шины, подписка,
    /// PushDeferred одного сообщения. Кадры ждет GDScript-обертка.
    /// </summary>
    public void BeginDeferredProbe()
    {
        EventBus.Reset();
        _deferredCalls.Clear();
        EventBus.Subscribe<DeferredProbeMsg>(msg => _deferredCalls.Add(1));
        EventBus.PushDeferred(new DeferredProbeMsg());
    }

    /// <summary>Число доставленных проб отложенной отправки.</summary>
    /// <returns>Счетчик вызовов подписчика пробы.</returns>
    public int DeferredCallCount() => _deferredCalls.Count;

    /// <summary>Конец пробы отложенной отправки: сброс шины.</summary>
    public void EndDeferredProbe() => EventBus.Reset();

    /// <summary>Поиск всех неабстрактных наследников <see cref="CsTestCase"/> в сборке.</summary>
    /// <returns>Список типов тестовых наборов по алфавиту.</returns>
    private static List<Type> FindSuites() =>
        [.. Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type.IsSubclassOf(typeof(CsTestCase)) && !type.IsAbstract)
            .OrderBy(type => type.Name)];

    /// <summary>Поиск методов Test* типа <paramref name="type"/> в порядке объявления.</summary>
    /// <param name="type">Тип тестового набора.</param>
    /// <returns>Список тестовых методов.</returns>
    private static List<MethodInfo> FindTestMethods(Type type) =>
        [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Test") && method.GetParameters().Length == 0)
            .OrderBy(method => method.MetadataToken)];

    /// <summary>Снятие обертки рефлексии с исключения тестового метода.</summary>
    /// <param name="error">Пойманное исключение.</param>
    /// <returns>Исходное исключение теста.</returns>
    private static Exception Unwrap(Exception error) =>
        error is TargetInvocationException { InnerException: not null } wrapped
            ? wrapped.InnerException
            : error;

    /// <summary>Сообщение-проба отложенной отправки.</summary>
    private class DeferredProbeMsg : EventBus.Message;
}
