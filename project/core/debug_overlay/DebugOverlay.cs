using System.Collections.Generic;

using Godot;


/// <summary>
/// Класс отображения отладочного слоя.
/// Выводит на экран последние сообщения шины.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    /// <summary>
    /// Сколько последних строк лога держим на экране.
    /// </summary>
    public const int LogLines = 8;


    /// <summary>
    /// Кольцевой буфер строк лога.
    /// Старые в голове, новые в хвосте.
    /// </summary>
    private readonly Queue<string> _log = [];

    /// <summary>
    /// Ссылка на экземпляр вывода текстов лога.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private RichTextLabel _logOutput = null!;



    /// <summary>
    /// Метод инициализации: подписка на все сообщения шины.
    /// </summary>
    public void Init() => EventBus.Subscribe<EventBus.Message>(OnMessage);


    /// <summary>
    /// Метод деинициализации.
    /// </summary>
    public void Deinit()
    {
        EventBus.Unsubscribe<EventBus.Message>(OnMessage);
        _log.Clear();
        RenderLog();
    }


    /// <summary>
    /// Метод, вызываемый при первом кадре.
    /// </summary>
    public override void _Ready() => _logOutput = GetNode<RichTextLabel>("%Log");


    /// <summary>
    /// Метод отрисовки логов.
    /// </summary>
    private void RenderLog() => _logOutput.Text = string.Join("\n", _log);


    /// <summary>
    /// Метод, вызываемый при получении любого сообщения шины: печать и экранный лог.
    /// </summary>
    /// <param name="message">Полученное сообщение.</param>
    private void OnMessage(EventBus.Message message)
    {
        string line = message.ToString();
        GD.Print(line);

        // Рисуем лог.
        _log.Enqueue(line);
        if (_log.Count > LogLines)
        {
            _log.Dequeue();
        }
        RenderLog();
    }
}
