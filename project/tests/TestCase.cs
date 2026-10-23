using System.Collections.Generic;

/// <summary>Базовый класс тестов Godot-части, тесты - публичные методы Test*.</summary>
public abstract class TestCase
{
    /// <summary>Ошибки текущего теста.</summary>
    public List<string> Failures { get; } = [];

    /// <summary>Число проверок, выполненных текущим тестом.</summary>
    public int Checks { get; private set; }

    /// <summary>Подготовка перед каждым тестом.</summary>
    public virtual void BeforeEach()
    {
    }

    /// <summary>Уборка после каждого теста.</summary>
    public virtual void AfterEach()
    {
    }

    /// <summary>Сброс счётчиков перед следующим тестом.</summary>
    public void Reset()
    {
        Failures.Clear();
        Checks = 0;
    }

    /// <summary>Проверка, что условие истинно.</summary>
    protected void Check(bool condition, string message)
    {
        Checks++;
        if (!condition)
        {
            Failures.Add(message);
        }
    }

    /// <summary>Проверка, что числа равны с допуском tolerance.</summary>
    protected void CheckNear(double expected, double actual, double tolerance, string message)
    {
        bool near = System.Math.Abs(expected - actual) <= tolerance;
        Check(near, $"{message}: expected {expected} +- {tolerance}, got {actual}");
    }

    /// <summary>Проверка, что значения равны.</summary>
    protected void CheckEqual<T>(T expected, T actual, string message)
    {
        bool equal = EqualityComparer<T>.Default.Equals(expected, actual);
        Check(equal, $"{message}: expected {expected}, got {actual}");
    }
}
