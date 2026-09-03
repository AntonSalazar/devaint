using System;
using System.Collections.Generic;

/// <summary>
/// Базовый класс C#-теста — зеркало GDScript-версии test_case.gd.
/// Наследники объявляют методы Test* — мост <see cref="CsTestBridge"/>
/// вызывает их по одному. Проверки — через Check*; провалы копятся,
/// тест не прерывается.
/// </summary>
public abstract class CsTestCase
{
    /// <summary>Провалы текущего тестового метода.</summary>
    public List<string> Failures { get; } = [];

    /// <summary>Количество проверок, выполненных текущим тестовым методом.</summary>
    public int Checks { get; set; }

    /// <summary>Вызывается перед каждым тестовым методом.</summary>
    public virtual void BeforeEach()
    {
    }

    /// <summary>Вызывается после каждого тестового метода.</summary>
    public virtual void AfterEach()
    {
    }

    /// <summary>Проверка истинности условия <paramref name="condition"/>.</summary>
    /// <param name="condition">Проверяемое условие.</param>
    /// <param name="what">Описание проверки для отчета.</param>
    public void CheckTrue(bool condition, string what)
    {
        Checks += 1;
        if (!condition)
        {
            Failures.Add(what);
        }
    }

    /// <summary>Проверка равенства <paramref name="got"/> ожидаемому <paramref name="expected"/>.</summary>
    /// <param name="got">Полученное значение.</param>
    /// <param name="expected">Ожидаемое значение.</param>
    /// <param name="what">Описание проверки для отчета.</param>
    /// <typeparam name="T">Тип сравниваемых значений.</typeparam>
    public void CheckEq<T>(T got, T expected, string what)
    {
        Checks += 1;
        if (!EqualityComparer<T>.Default.Equals(got, expected))
        {
            Failures.Add($"{what}: expected `{expected}`, got `{got}`");
        }
    }

    /// <summary>
    /// Проверка близости числа <paramref name="got"/> к <paramref name="expected"/>
    /// с допуском <paramref name="tolerance"/>.
    /// </summary>
    /// <param name="got">Полученное значение.</param>
    /// <param name="expected">Ожидаемое значение.</param>
    /// <param name="what">Описание проверки для отчета.</param>
    /// <param name="tolerance">Допустимое отклонение.</param>
    public void CheckNear(double got, double expected, string what, double tolerance = 0.00001)
    {
        Checks += 1;
        if (Math.Abs(got - expected) > tolerance)
        {
            Failures.Add($"{what}: expected ~`{expected}`, got `{got}`");
        }
    }

    /// <summary>Безусловный провал.</summary>
    /// <param name="what">Описание провала для отчета.</param>
    public void Fail(string what)
    {
        Checks += 1;
        Failures.Add(what);
    }
}
