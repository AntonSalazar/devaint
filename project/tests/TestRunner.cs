using System;
using System.Linq;
using System.Reflection;
using Godot;

/// <summary>Запуск всех тестов Godot-части и выход с кодом 0 или 1.</summary>
public partial class TestRunner : Node
{
    /// <summary>Прогон тестов при старте сцены.</summary>
    public override void _Ready()
    {
        int failed = 0;
        int passed = 0;

        foreach (Type type in FindSuites())
        {
            GD.Print($"\n=== {type.Name} ===");
            TestCase suite = (TestCase)Activator.CreateInstance(type)!;
            foreach (MethodInfo method in FindTests(type))
            {
                if (Run(suite, method))
                {
                    passed++;
                }
                else
                {
                    failed++;
                }
            }
        }

        GD.Print($"\n{passed} passed, {failed} failed");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }

    /// <summary>Все неабстрактные наследники TestCase, по имени.</summary>
    private static Type[] FindSuites()
    {
        return [.. Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => type.IsSubclassOf(typeof(TestCase)) && !type.IsAbstract)
            .OrderBy(type => type.Name)];
    }

    /// <summary>Публичные методы Test* без параметров, по имени.</summary>
    private static MethodInfo[] FindTests(Type type)
    {
        return [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.StartsWith("Test", StringComparison.Ordinal))
            .Where(method => method.GetParameters().Length == 0)
            .OrderBy(method => method.Name)];
    }

    /// <summary>Запуск одного теста с отчётом, true - тест прошёл.</summary>
    private static bool Run(TestCase suite, MethodInfo method)
    {
        suite.Reset();
        suite.BeforeEach();
        try
        {
            method.Invoke(suite, null);
        }
        catch (TargetInvocationException error)
        {
            suite.Failures.Add($"exception: {error.InnerException?.Message}");
        }

        suite.AfterEach();

        // Тест без единой проверки считается упавшим: скорее всего, он ничего не проверяет
        if (suite.Checks == 0 && suite.Failures.Count == 0)
        {
            suite.Failures.Add("no checks executed");
        }

        string title = $"{method.DeclaringType!.Name}.{method.Name}";
        if (suite.Failures.Count == 0)
        {
            GD.Print($"  PASS  {title} - {suite.Checks} checks");
            return true;
        }

        GD.PrintErr($"  FAIL  {title}");
        foreach (string failure in suite.Failures)
        {
            GD.PrintErr($"        {failure}");
        }

        return false;
    }
}
