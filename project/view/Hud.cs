using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>HUD поверх мира: полосы статов и список мудлов своего персонажа.</summary>
public partial class Hud : CanvasLayer
{
    /// <summary>Строки статов: подпись и полоса.</summary>
    private readonly List<(Label Name, ProgressBar Bar)> _rows = [];

    /// <summary>Колонка мудлов.</summary>
    private readonly VBoxContainer _moodles = new() { Name = "Moodles" };

    /// <summary>Колонка предметов.</summary>
    private readonly VBoxContainer _items = new() { Name = "Items" };

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

    /// <summary>Строки мудлов, для тестов.</summary>
    public IReadOnlyList<string> MoodleLines => Lines(_moodles);

    /// <summary>Строки предметов, для тестов.</summary>
    public IReadOnlyList<string> ItemLines => Lines(_items);

    /// <summary>Доли полос статов, для тестов.</summary>
    public IReadOnlyList<double> BarValues => _rows.ConvertAll(row => row.Bar.Value);

    /// <summary>Видна ли надпись о смерти.</summary>
    public bool DeathVisible => _death.Visible;

    /// <summary>Текст надписи о смерти на текущем языке.</summary>
    public string DeathText => _death.Text;

    /// <summary>Постройка панели по числу статов; вызывается сразу после создания.</summary>
    public void Init(HudModel model)
    {
        Name = "Hud";
        _model = model;
        PanelContainer panel = new() { Name = "Panel", Position = new Vector2(16, 16) };
        VBoxContainer column = new() { Name = "Column", CustomMinimumSize = new Vector2(220, 0) };
        panel.AddChild(column);
        AddChild(panel);

        foreach (StatBar bar in model.Bars())
        {
            Label name = new() { Text = bar.Name };
            ProgressBar progress = new() { MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
            column.AddChild(name);
            column.AddChild(progress);
            _rows.Add((name, progress));
        }

        column.AddChild(_moodles);
        column.AddChild(new HSeparator());
        column.AddChild(_items);
        _death.AddThemeFontSizeOverride("font_size", 48);
        AddChild(_death);
        Refresh();
    }

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

        IReadOnlyList<StatBar> bars = _model.Bars();
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Name.Text = $"{bars[i].Name} {bars[i].Value:0}";
            _rows[i].Bar.Value = bars[i].Fraction;
        }

        ShowLines(_moodles, _model.Moodles(), new Color(1f, 0.8f, 0.4f));
        ShowLines(_items, _model.Items(), Colors.White);
    }

    /// <summary>Тексты подписей колонки.</summary>
    private static List<string> Lines(VBoxContainer column)
    {
        List<string> lines = [];
        foreach (Node child in column.GetChildren())
        {
            lines.Add(((Label)child).Text);
        }

        return lines;
    }

    /// <summary>Подписи колонки по строкам; пересоздаются только при изменении, а не каждый кадр.</summary>
    private static void ShowLines(VBoxContainer column, IReadOnlyList<string> lines, Color color)
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
}
