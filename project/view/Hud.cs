using System;
using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Defs;
using Godot;

/// <summary>HUD поверх мира: панели модов по углам экрана и надпись о смерти.</summary>
public partial class Hud : CanvasLayer
{
    /// <summary>Отступ панелей от края экрана.</summary>
    private const int Margin = 16;

    /// <summary>Цвет строк мудлов.</summary>
    private static readonly Color _moodleColor = new(1f, 0.8f, 0.4f);

    /// <summary>Панели с узлами, для обновления видимости.</summary>
    private readonly List<(UiPanelDef Def, PanelContainer Node)> _panels = [];

    /// <summary>Строки панелей с их узлами, для обновления значений.</summary>
    private readonly List<(UiRowDef Row, Control Node)> _rows = [];

    /// <summary>Надпись о смерти по центру экрана.</summary>
    private readonly Label _death = new()
    {
        Name = "Death",
        Text = "ui.you_died",

        // Текст переводится явно в Refresh, автоматический перевод Godot его бы повторил
        AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        Visible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        AnchorRight = 1,
        AnchorBottom = 1,
        Modulate = new Color(1f, 0.3f, 0.25f),
    };

    /// <summary>Данные HUD.</summary>
    private HudModel? _model;

    /// <summary>Нажата кнопка панели: номер действия для сервера.</summary>
    public event Action<int>? ActionPressed;

    /// <summary>Строки мудлов, для тестов.</summary>
    public IReadOnlyList<string> MoodleLines => Lines(RowNodes(row => row.Moodles));

    /// <summary>Строки предметов, для тестов.</summary>
    public IReadOnlyList<string> ItemLines => Lines(RowNodes(row => row.Items));

    /// <summary>Доли полос статов по порядку строк, для тестов.</summary>
    public IReadOnlyList<double> BarValues => [.. RowNodes(row => row.Stat is not null).Select(node => ((ProgressBar)node.GetChild(1)).Value)];

    /// <summary>Тексты строк-текстов по порядку, для тестов.</summary>
    public IReadOnlyList<string> TextLines => [.. RowNodes(row => row.Text is not null).Select(node => ((Label)node).Text)];

    /// <summary>Видна ли надпись о смерти.</summary>
    public bool DeathVisible => _death.Visible;

    /// <summary>Текст надписи о смерти на текущем языке.</summary>
    public string DeathText => _death.Text;

    /// <summary>Постройка панелей по данным модов; вызывается сразу после создания.</summary>
    public void Init(HudModel model)
    {
        Name = "Hud";
        _model = model;
        Dictionary<UiAnchor, VBoxContainer> corners = [];
        foreach (UiPanelDef def in model.Panels)
        {
            if (!corners.TryGetValue(def.Anchor, out VBoxContainer? corner))
            {
                corner = Corner(def.Anchor);
                corners[def.Anchor] = corner;
                AddChild(corner);
            }

            PanelContainer panel = new() { Name = def.Id.Replace(':', '_') };
            VBoxContainer column = new() { Name = "Column", CustomMinimumSize = new Vector2(220, 0) };
            panel.AddChild(column);
            corner.AddChild(panel);
            if (def.Title is not null)
            {
                column.AddChild(new Label { Name = "Title", Text = HudModel.Name(def.Title), Modulate = _moodleColor });
            }

            foreach (UiRowDef row in def.Rows)
            {
                Control node = RowNode(row);
                column.AddChild(node);
                _rows.Add((row, node));
            }

            _panels.Add((def, panel));
        }

        _death.AddThemeFontSizeOverride("font_size", 48);
        AddChild(_death);
        Refresh();
    }

    /// <summary>Нажатие кнопки с действием action, как мышью, для тестов.</summary>
    public void Press(string action)
    {
        foreach ((UiRowDef row, Control node) in _rows)
        {
            if (row.Action == action)
            {
                ((Button)node).EmitSignal(BaseButton.SignalName.Pressed);
                return;
            }
        }

        throw new ArgumentException($"no button with action {action}", nameof(action));
    }

    /// <summary>Видна ли панель id, для тестов.</summary>
    public bool PanelVisible(string id) => _panels.Single(panel => panel.Def.Id == id).Node.Visible;

    /// <summary>Обновление по текущей реплике через delta секунд после прошлого; дёшево, вызывается каждый кадр.</summary>
    public void Refresh(double delta = 0)
    {
        if (_model is null)
        {
            return;
        }

        _model.Advance(delta);
        _death.Visible = _model.DeathVisible;
        _death.Text = GameLocale.Text("ui.you_died", "You died");
        foreach ((UiPanelDef def, PanelContainer node) in _panels)
        {
            node.Visible = _model.Visible(def);
        }

        foreach ((UiRowDef row, Control node) in _rows)
        {
            RefreshRow(_model, row, node);
        }
    }

    /// <summary>Тексты подписей колонок.</summary>
    private static List<string> Lines(IEnumerable<Control> columns)
    {
        List<string> lines = [];
        foreach (Control column in columns)
        {
            lines.AddRange(Lines(column));
        }

        return lines;
    }

    /// <summary>Тексты подписей колонки.</summary>
    private static List<string> Lines(Control column)
    {
        List<string> lines = [];
        foreach (Node child in column.GetChildren())
        {
            lines.Add(((Label)child).Text);
        }

        return lines;
    }

    /// <summary>Подписи колонки по строкам; пересоздаются только при изменении, а не каждый кадр.</summary>
    private static void ShowLines(Control column, IReadOnlyList<string> lines, Color color)
    {
        if (Lines(column).SequenceEqual(lines))
        {
            return;
        }

        foreach (Node child in column.GetChildren())
        {
            column.RemoveChild(child);
            child.QueueFree();
        }

        foreach (string line in lines)
        {
            column.AddChild(new Label { Text = line, Modulate = color });
        }
    }

    /// <summary>Колонка панелей угла anchor, растёт от угла внутрь экрана.</summary>
    private static VBoxContainer Corner(UiAnchor anchor)
    {
        bool right = anchor is UiAnchor.TopRight or UiAnchor.BottomRight;
        bool bottom = anchor is UiAnchor.BottomLeft or UiAnchor.BottomRight;
        VBoxContainer corner = new()
        {
            Name = anchor.ToString(),
            AnchorLeft = right ? 1 : 0,
            AnchorRight = right ? 1 : 0,
            AnchorTop = bottom ? 1 : 0,
            AnchorBottom = bottom ? 1 : 0,
            OffsetLeft = right ? -Margin : Margin,
            OffsetRight = right ? -Margin : Margin,
            OffsetTop = bottom ? -Margin : Margin,
            OffsetBottom = bottom ? -Margin : Margin,
            GrowHorizontal = right ? Control.GrowDirection.Begin : Control.GrowDirection.End,
            GrowVertical = bottom ? Control.GrowDirection.Begin : Control.GrowDirection.End,
        };
        return corner;
    }

    /// <summary>Значения строки по модели: полоса, текст, подпись кнопки или колонка подписей.</summary>
    private static void RefreshRow(HudModel model, UiRowDef row, Control node)
    {
        if (row.Stat is not null)
        {
            StatBar bar = model.Bar(row.Stat);
            SetText((Label)node.GetChild(0), $"{bar.Name} {bar.Value:0}");
            ((ProgressBar)node.GetChild(1)).Value = bar.Fraction;
        }
        else if (row.Text is not null)
        {
            SetText((Label)node, model.Text(row.Text));
        }
        else if (row.Button is not null)
        {
            Button button = (Button)node;
            string text = model.Text(row.Button);
            if (button.Text != text)
            {
                button.Text = text;
            }
        }
        else if (row.Moodles)
        {
            ShowLines(node, model.Moodles(), _moodleColor);
        }
        else if (row.Items)
        {
            ShowLines(node, model.Items(), Colors.White);
        }
    }

    /// <summary>Текст подписи, только если он сменился: так подпись не перестраивается каждый кадр.</summary>
    private static void SetText(Label label, string text)
    {
        if (label.Text != text)
        {
            label.Text = text;
        }
    }

    /// <summary>Строки панели одного вида.</summary>
    private IEnumerable<Control> RowNodes(Func<UiRowDef, bool> kind) => _rows.Where(pair => kind(pair.Row)).Select(pair => pair.Node);

    /// <summary>Узел строки по её виду; значения ставит RefreshRow.</summary>
    private Control RowNode(UiRowDef row)
    {
        if (row.Stat is not null)
        {
            VBoxContainer stat = new() { Name = row.Stat.Replace(':', '_') };
            stat.AddChild(new Label());
            stat.AddChild(new ProgressBar { MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) });
            return stat;
        }

        if (row.Button is not null)
        {
            Button button = new() { Name = row.Action!.Replace(':', '_') };
            int action = _model!.ActionIndex(row.Action!);
            button.Pressed += () => ActionPressed?.Invoke(action);
            return button;
        }

        if (row.Separator)
        {
            return new HSeparator();
        }

        return row.Text is not null ? new Label() : new VBoxContainer { Name = row.Moodles ? "Moodles" : "Items" };
    }
}
