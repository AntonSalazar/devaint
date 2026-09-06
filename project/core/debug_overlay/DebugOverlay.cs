using System;
using System.Collections.Generic;

using Godot;


/// <summary>
/// Класс отображения отладочного слоя.
/// Выводит техническую информацию для отладки.
/// </summary>
public partial class DebugOverlay : CanvasLayer
{
    /// <summary>
    /// Сколько последних строк лога держим на экране.
    /// </summary>
    public const int LogLines = 8;


    /// <summary>
    /// Список исключения сообщений. Они не будут выводить в логи.
    /// </summary>
    private static readonly Type[] _logSkip = [typeof(GameClock.OnMinutePassed)];

    /// <summary>
    /// Список цвета уровней тревоги.
    /// </summary>
    private static readonly Color[] _noticeLevelColors =
        [new("#4de6d9"), Colors.Yellow, Colors.Orange, Colors.Red, Colors.Red];


    /// <summary>
    /// Кольцевой буфер строк лога.
    /// Старые в голове, новые в хвосте.
    /// </summary>
    private readonly Queue<string> _log = [];


    /// <summary>
    /// Ссылка на экземпляр таймера игрового времени.
    /// </summary>
    private GameClock? _clock = null;

    /// <summary>
    /// Ссылка на экземпляр робота.
    /// </summary>
    private Robot? _robot = null;

    /// <summary>
    /// Ссылка на экземпляр вывода текстов.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private RichTextLabel _output = null!;

    /// <summary>
    /// Ссылка на экземпляр шкалы значения тревоги.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private ProgressBar _noticeBar = null!;

    /// <summary>
    /// Ссылка на экземпляр вывода уровня тревоги.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private Label _noticeLevel = null!;

    /// <summary>
    /// Ссылка на экземпляр вывода текстов лога.
    /// Заполняется в <see cref="_Ready()"/>.
    /// </summary>
    private RichTextLabel _logOutput = null!;



    /// <summary>
    /// Метод инициализации.
    /// </summary>
    /// <param name="clock">Ссылка на экземпляр таймера игрового времени.</param>
    /// <param name="robot">Ссылка на экземпляр робота.</param>
    public void Init(GameClock clock, Robot robot)
    {
        _clock = clock;
        _robot = robot;
        EventBus.Subscribe<EventBus.Message>(OnMessage);
        SetProcess(true);
    }


    /// <summary>
    /// Метод деинициализации.
    /// </summary>
    public void Deinit()
    {
        SetProcess(false);
        EventBus.Unsubscribe<EventBus.Message>(OnMessage);
        _clock = null;
        _robot = null;
        _log.Clear();
        RenderLog();
    }


    /// <summary>
    /// Метод, вызываемый при первом кадре.
    /// </summary>
    public override void _Ready()
    {
        _output = GetNode<RichTextLabel>("%Output");
        _noticeBar = GetNode<ProgressBar>("%NoticeBar");
        _noticeLevel = GetNode<Label>("%NoticeLevel");
        _logOutput = GetNode<RichTextLabel>("%Log");
        SetProcess(false);
    }


    /// <summary>
    /// Метод процессинга.
    /// </summary>
    /// <param name="delta">Время между кадрами.</param>
    public override void _Process(double delta)
    {
        if (_robot is null || _clock is null)
        {
            return;
        }

        float notice = _robot.Notice;
        _output.Text = $"""
            {_clock.DatetimeStr} x{_clock.Speed} progress {_clock.DayProgress:F4}
            battery {_robot.Battery:F1}%
            notice {notice:F1}%
            """;

        // Обновим шкалу.
        SignalGrid.Level level = _robot.NoticeLevel;
        _noticeBar.Value = notice;
        _noticeBar.Modulate = _noticeLevelColors[(int)level];
        _noticeLevel.Text = $"NOTICE {notice:F1}% {level}";
    }


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
        if (_clock is null)
        {
            return;
        }
        string line = $"[{_clock.DatetimeStr}] {message}";
        GD.Print(line);

        // Пропускаем шумные сообщения.
        foreach (Type skip in _logSkip)
        {
            if (skip.IsInstanceOfType(message))
            {
                return;
            }
        }

        // Рисуем лог.
        _log.Enqueue(line);
        if (_log.Count > LogLines)
        {
            _log.Dequeue();
        }
        RenderLog();
    }
}
