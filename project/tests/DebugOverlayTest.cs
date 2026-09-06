using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="DebugOverlay"/>: шкала заметности (значение, уровень, цвет),
/// экранный лог (строки, фильтр шума, длина буфера, очистка).
/// Оверлей и робот инстанцируются из своих сцен.
/// </summary>
public class DebugOverlayTest : CsTestCase
{
    /// <summary>Сцена оверлея.</summary>
    private const string OverlayScenePath = "res://core/debug_overlay/debug_overlay.tscn";

    /// <summary>Сцена робота.</summary>
    private const string RobotScenePath = "res://core/robot/robot.tscn";

    /// <summary>Узлы текущего теста - освобождаются в AfterEach.</summary>
    private readonly List<Node> _nodes = [];

    /// <summary>Сброс шины перед тестом.</summary>
    public override void BeforeEach() => EventBus.Reset();

    /// <summary>Освобождение узлов и сброс шины после теста.</summary>
    public override void AfterEach()
    {
        foreach (Node node in _nodes)
        {
            node.Free();
        }
        _nodes.Clear();
        EventBus.Reset();
    }

    /// <summary>Шкала показывает заметность робота, уровень и цвет уровня.</summary>
    public void TestNoticeBarFollowsRobot()
    {
        SignalGrid grid = new(new Vector2I(-10, -10), new Vector2I(21, 21));
        grid.AddTower(Vector2I.Zero, 300.0f);
        Robot robot = SpawnRobot();
        robot.Init(new GameClock(), grid);
        robot.GlobalPosition = Iso.CellToWorld(Vector2I.Zero);
        DebugOverlay overlay = SpawnOverlay();
        overlay.Init(new GameClock(), robot);

        grid.Accumulate(robot.GlobalPosition, 25.0f / SignalGrid.NoticeRate, 1.0f);
        overlay._Process(0.0);
        ProgressBar bar = overlay.GetNode<ProgressBar>("%NoticeBar");
        Label label = overlay.GetNode<Label>("%NoticeLevel");
        CheckNear(bar.Value, 25.0, "bar value equals robot notice", 0.001);
        CheckTrue(label.Text.Contains("Curious"), "label names the level");
        CheckEq(bar.Modulate, Colors.Yellow, "bar is tinted by the Curious color");
        overlay.Deinit();
        robot.Deinit();
    }

    /// <summary>Лог: сообщение попадает на экран с игровым таймстампом, шум - нет.</summary>
    public void TestLogLinesAndFilter()
    {
        DebugOverlay overlay = SpawnOverlay();
        Robot robot = SpawnRobot();
        overlay.Init(new GameClock(), robot);
        RichTextLabel logOutput = overlay.GetNode<RichTextLabel>("%Log");

        new GameClock.OnHourPassed(60, new GameClock.GameTime(Day: 1, Hour: 1, Minute: 0)).Push();
        CheckTrue(logOutput.Text.Contains("GameClock.OnHourPassed"), "hour tick is logged");
        CheckTrue(logOutput.Text.StartsWith("[Day 1 "), "line starts with a game timestamp");
        int linesBefore = logOutput.Text.Split('\n').Length;
        new GameClock.OnMinutePassed(61, new GameClock.GameTime(Day: 1, Hour: 1, Minute: 1)).Push();
        CheckEq(logOutput.Text.Split('\n').Length, linesBefore, "minute tick is filtered out");
        overlay.Deinit();
    }

    /// <summary>Лог держит не больше LogLines строк, старые выпадают с начала.</summary>
    public void TestLogRingBuffer()
    {
        DebugOverlay overlay = SpawnOverlay();
        Robot robot = SpawnRobot();
        overlay.Init(new GameClock(), robot);
        RichTextLabel logOutput = overlay.GetNode<RichTextLabel>("%Log");

        for (int idx = 1; idx <= DebugOverlay.LogLines + 2; idx++)
        {
            new SignalGrid.OnNoticeLevelChanged(
                new Vector2I(idx, 0), SignalGrid.Level.None, SignalGrid.Level.Curious, idx).Push();
        }
        string[] lines = logOutput.Text.Split('\n');
        CheckEq(lines.Length, DebugOverlay.LogLines, "buffer is capped at LogLines");
        CheckTrue(lines[0].Contains("(3, 0)"), "oldest kept line is the third published");
        CheckTrue(lines[^1].Contains($"({DebugOverlay.LogLines + 2}, 0)"), "newest line is last");
        overlay.Deinit();
    }

    /// <summary>Deinit очищает лог.</summary>
    public void TestDeinitClearsLog()
    {
        DebugOverlay overlay = SpawnOverlay();
        Robot robot = SpawnRobot();
        overlay.Init(new GameClock(), robot);
        RichTextLabel logOutput = overlay.GetNode<RichTextLabel>("%Log");

        new GameClock.OnHourPassed(60, new GameClock.GameTime(Day: 1, Hour: 1, Minute: 0)).Push();
        CheckTrue(logOutput.Text.Length > 0, "log has a line before Deinit");
        overlay.Deinit();
        CheckEq(logOutput.Text, string.Empty, "log output is cleared on Deinit");
    }

    /// <summary>Создание оверлея из сцены с добавлением в корень дерева.</summary>
    /// <returns>Оверлей, освобождаемый в AfterEach.</returns>
    private DebugOverlay SpawnOverlay() => Spawn<DebugOverlay>(OverlayScenePath);

    /// <summary>Создание робота из сцены с добавлением в корень дерева.</summary>
    /// <returns>Робот, освобождаемый в AfterEach.</returns>
    private Robot SpawnRobot() => Spawn<Robot>(RobotScenePath);

    /// <summary>Инстанцирование сцены в корень дерева с учетом освобождения.</summary>
    /// <param name="path">Путь к сцене.</param>
    /// <typeparam name="T">Тип корня сцены.</typeparam>
    /// <returns>Корень сцены.</returns>
    private T Spawn<T>(string path)
        where T : Node
    {
        T node = GD.Load<PackedScene>(path).Instantiate<T>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
        _nodes.Add(node);
        return node;
    }
}
