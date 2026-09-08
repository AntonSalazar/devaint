using System.Collections.Generic;
using Godot;

/// <summary>
/// Тесты <see cref="DebugOverlay"/>: экранный лог шины (строки, длина буфера, очистка).
/// Оверлей инстанцируется из своей сцены.
/// </summary>
public class DebugOverlayTest : CsTestCase
{
    /// <summary>Сцена оверлея.</summary>
    private const string OverlayScenePath = "res://core/debug_overlay/debug_overlay.tscn";

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

    /// <summary>Лог: любое сообщение шины попадает на экран своим именем типа.</summary>
    public void TestLogLine()
    {
        DebugOverlay overlay = SpawnOverlay();
        overlay.Init();
        RichTextLabel logOutput = overlay.GetNode<RichTextLabel>("%Log");

        new ProbeMsg(1).Push();
        CheckTrue(logOutput.Text.Contains("DebugOverlayTest.ProbeMsg"), "message type is logged");
        CheckTrue(logOutput.Text.EndsWith("#1"), "message payload is logged");
        overlay.Deinit();
    }

    /// <summary>Лог держит не больше LogLines строк, старые выпадают с начала.</summary>
    public void TestLogRingBuffer()
    {
        DebugOverlay overlay = SpawnOverlay();
        overlay.Init();
        RichTextLabel logOutput = overlay.GetNode<RichTextLabel>("%Log");

        for (int idx = 1; idx <= DebugOverlay.LogLines + 2; idx++)
        {
            new ProbeMsg(idx).Push();
        }
        string[] lines = logOutput.Text.Split('\n');
        CheckEq(lines.Length, DebugOverlay.LogLines, "buffer is capped at LogLines");
        CheckTrue(lines[0].EndsWith("#3"), "oldest kept line is the third published");
        CheckTrue(lines[^1].EndsWith($"#{DebugOverlay.LogLines + 2}"), "newest line is last");
        overlay.Deinit();
    }

    /// <summary>Deinit очищает лог и отписывает от шины.</summary>
    public void TestDeinitClearsLog()
    {
        DebugOverlay overlay = SpawnOverlay();
        overlay.Init();
        RichTextLabel logOutput = overlay.GetNode<RichTextLabel>("%Log");

        new ProbeMsg(1).Push();
        CheckTrue(logOutput.Text.Length > 0, "log has a line before Deinit");
        overlay.Deinit();
        CheckEq(logOutput.Text, string.Empty, "log output is cleared on Deinit");
        CheckEq(EventBus.SubscriptionCount, 0, "overlay is unsubscribed on Deinit");
    }

    /// <summary>Создание оверлея из сцены с добавлением в корень дерева.</summary>
    /// <returns>Оверлей, освобождаемый в AfterEach.</returns>
    private DebugOverlay SpawnOverlay()
    {
        DebugOverlay overlay = GD.Load<PackedScene>(OverlayScenePath).Instantiate<DebugOverlay>();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(overlay);
        _nodes.Add(overlay);
        return overlay;
    }

    /// <summary>Тестовое сообщение с номером для различения строк лога.</summary>
    /// <param name="index">Номер сообщения.</param>
    public class ProbeMsg(int index) : EventBus.Message
    {
        /// <summary>Номер сообщения.</summary>
        public int Index { get; } = index;

        /// <summary>Имя типа и номер - чтобы строки лога различались.</summary>
        /// <returns>Строка вида `DebugOverlayTest.ProbeMsg #3`.</returns>
        public override string ToString() => $"{base.ToString()} #{Index}";
    }
}
