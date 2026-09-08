using Godot;

/// <summary>
/// Тесты <see cref="Main"/> на настоящей сцене main.tscn: запуск цикла,
/// teardown с очисткой шины, повторный teardown.
/// </summary>
public class MainTest : CsTestCase
{
    /// <summary>Сцена корня игры.</summary>
    private const string MainScenePath = "res://core/main/main.tscn";

    /// <summary>Полный сброс шины перед каждым тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Сброс шины после теста.</summary>
    public override void AfterEach() => EventBus.Reset();

    /// <summary>Запуск: цикл идет, оверлей подписан на шину.</summary>
    public void TestLaunch()
    {
        Main main = SpawnMain();

        CheckTrue(main.IsRunning, "main is running after launch");
        CheckTrue(EventBus.SubscriptionCount > 0, "overlay is subscribed to the bus");
        main.Free();
    }

    /// <summary>Teardown: выход из дерева чистит шину и останавливает цикл.</summary>
    public void TestTeardownResetsBus()
    {
        Main main = SpawnMain();

        CheckTrue(EventBus.SubscriptionCount > 0, "bus has subscriptions while running");
        main.Free();
        CheckEq(EventBus.SubscriptionCount, 0, "bus is clean after teardown");
    }

    /// <summary>Повторный teardown (WM_CLOSE_REQUEST + выход из дерева) безопасен.</summary>
    public void TestDoubleTeardownIsSafe()
    {
        Main main = SpawnMain();

        main.Notification((int)Node.NotificationWMCloseRequest);
        CheckTrue(!main.IsRunning, "close request stops the loop");
        CheckEq(EventBus.SubscriptionCount, 0, "close request cleans the bus");
        main.Free();
        CheckTrue(true, "second teardown on exit does not break");
    }

    /// <summary>Создание Main из сцены с добавлением в корень дерева: вход в дерево запускает цикл.</summary>
    /// <returns>Корень игры; тест освобождает его сам через Free().</returns>
    private static Main SpawnMain()
    {
        Main main = GD.Load<PackedScene>(MainScenePath).Instantiate<Main>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(main);
        return main;
    }
}
