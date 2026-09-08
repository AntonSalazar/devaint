using Godot;

/// <summary>
/// Класс Main игры.
/// Опорная точка, откуда всё начинается и где всё заканчивается.
/// </summary>
public partial class Main : Node
{
    /// <summary>
    /// Ссылка на экземпляр вывода отладки.
    /// Заполняется в <see cref="_Ready"/>.
    /// </summary>
    private DebugOverlay _debugOverlay = null!;

    /// <summary>
    /// Флаг запущенного цикла. Есть только между Launch и Teardown.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Метод, вызываемый при первом кадре: запуск цикла игры.
    /// </summary>
    public override void _Ready()
    {
        _debugOverlay = GetNode<DebugOverlay>("%DebugOverlay");
        Launch();
    }

    /// <summary>
    /// Метод, вызываемый при получении уведомления от движка.
    /// </summary>
    /// <param name="what">Код уведомления.</param>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            Teardown();
        }
    }

    /// <summary>
    /// Метод, вызываемый при выходе из дерева.
    /// </summary>
    public override void _ExitTree() => Teardown();

    /// <summary>
    /// Метод запуска работы цикла.
    /// </summary>
    private void Launch()
    {
        // Добавляем отладочный гуй.
        _debugOverlay.Init();
        IsRunning = true;
    }

    /// <summary>
    /// Метод завершения работы цикла. Повторный вызов безопасен.
    /// </summary>
    private void Teardown()
    {
        // Фильтруем повторное выключение.
        if (!IsRunning)
        {
            return;
        }

        GD.Print($"{nameof(Main)}: Teardown... Bye-bye!");
        IsRunning = false;

        // Сбрасываем шину.
        EventBus.Reset();

        // Сбрасываем ссылки.
        _debugOverlay.Deinit();
    }
}
