using System.Collections.Generic;
using Godot;

/// <summary>HUD поверх мира: полосы статов и список мудлов своего персонажа.</summary>
public partial class Hud : CanvasLayer
{
    /// <summary>Строки статов: подпись и полоса.</summary>
    private readonly List<(Label Name, ProgressBar Bar)> _rows = [];

    /// <summary>Колонка мудлов.</summary>
    private readonly VBoxContainer _moodles = new() { Name = "Moodles" };

    /// <summary>Данные HUD.</summary>
    private HudModel? _model;

    /// <summary>Строки мудлов, для тестов.</summary>
    public IReadOnlyList<string> MoodleLines
    {
        get
        {
            List<string> lines = [];
            foreach (Node child in _moodles.GetChildren())
            {
                lines.Add(((Label)child).Text);
            }

            return lines;
        }
    }

    /// <summary>Доли полос статов, для тестов.</summary>
    public IReadOnlyList<double> BarValues => _rows.ConvertAll(row => row.Bar.Value);

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
        Refresh();
    }

    /// <summary>Обновление по текущей реплике; дёшево, вызывается каждый кадр.</summary>
    public void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        IReadOnlyList<StatBar> bars = _model.Bars();
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Name.Text = $"{bars[i].Name} {bars[i].Value:0}";
            _rows[i].Bar.Value = bars[i].Fraction;
        }

        IReadOnlyList<string> moodles = _model.Moodles();
        if (!SameLines(moodles))
        {
            foreach (Node child in _moodles.GetChildren())
            {
                _moodles.RemoveChild(child);
                child.QueueFree();
            }

            foreach (string line in moodles)
            {
                _moodles.AddChild(new Label { Text = line, Modulate = new Color(1f, 0.8f, 0.4f) });
            }
        }
    }

    /// <summary>Совпадают ли строки мудлов с показанными, чтобы не пересоздавать подписи каждый кадр.</summary>
    private bool SameLines(IReadOnlyList<string> moodles)
    {
        IReadOnlyList<string> shown = MoodleLines;
        if (shown.Count != moodles.Count)
        {
            return false;
        }

        for (int i = 0; i < moodles.Count; i++)
        {
            if (shown[i] != moodles[i])
            {
                return false;
            }
        }

        return true;
    }
}
