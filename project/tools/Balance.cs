using System;
using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// Headless-прогон кривых заметности. Запуск: `just balance`.
/// Гоняет настоящие <see cref="GameClock"/> и <see cref="SignalGrid"/> без рендера
/// по сценариям (излучение x покрытие, спад) и печатает таблицы по минутам,
/// чтобы подбирать NoticeRate / Decay по числам, а не по ощущениям.
/// </summary>
public partial class Balance : SceneTree
{
    /// <summary>Горизонт сценария накопления, игровых минут.</summary>
    private const int HorizonMinutes = 60;

    /// <summary>Шаг печати строк таблицы, минут.</summary>
    private const int PrintEvery = 5;

    /// <summary>Излучение зарядки от сети (07-SIGNATURE-MATH.md; в коде робота еще нет).</summary>
    private const float ChargeEmission = 15.0f;

    /// <summary>Пороги реакций сети (07-SIGNATURE-MATH.md).</summary>
    private static readonly float[] _thresholds = [25.0f, 50.0f, 75.0f, 100.0f];

    /// <summary>Угол тестового грида.</summary>
    private static readonly Vector2I _origin = new(-12, -12);

    /// <summary>Размер тестового грида.</summary>
    private static readonly Vector2I _size = new(25, 25);

    /// <summary>Сценарии накопления: метка, излучение, ключ покрытия.</summary>
    private static readonly (string Label, float Emission, string Spot)[] _scenarios =
    [
        ("walk   @ 1.0", Robot.EmissionOf(Robot.State.Walk), "1.0"),
        ("idle   @ 1.0", Robot.EmissionOf(Robot.State.Idle), "1.0"),
        ("walk   @ 0.5", Robot.EmissionOf(Robot.State.Walk), "0.5"),
        ("walk   @ 0.2", Robot.EmissionOf(Robot.State.Walk), "0.2"),
        ("charge @ 1.0", ChargeEmission, "1.0"),
        ("charge @ 0.5", ChargeEmission, "0.5"),
    ];

    /// <summary>Точка входа: сброс шины, прогон, выход.</summary>
    public override void _Initialize()
    {
        EventBus.Reset();
        Run();
        EventBus.Reset();
        Quit(0);
    }

    /// <summary>Прогон всех сценариев.</summary>
    private static void Run()
    {
        GD.Print("=== DevAInt notice balance ===");
        GD.Print(
            $"NoticeRate = {SignalGrid.NoticeRate:F2}  DecayPerMinute = {SignalGrid.DecayPerMinute:F2}  "
            + $"AbsentMult = {SignalGrid.AbsentDecayMultiplier:F1}  TowerRadius = {TowerMarker.DefaultRadius:F0}");
        GD.Print(
            $"emission: idle {Robot.EmissionOf(Robot.State.Idle):F1}  "
            + $"walk {Robot.EmissionOf(Robot.State.Walk):F1}  charge {ChargeEmission:F1}");

        // Позиции ищем по покрытию, а не задаем руками.
        SignalGrid probe = MakeGrid();
        Dictionary<string, Vector2I> spots = new()
        {
            ["1.0"] = FindCellWithCoverage(probe, 1.0f),
            ["0.5"] = FindCellWithCoverage(probe, 0.5f),
            ["0.2"] = FindCellWithCoverage(probe, 0.2f),
        };
        GD.Print($"spots: full {spots["1.0"]}  half {spots["0.5"]}  edge {spots["0.2"]}");

        foreach ((string label, float emission, string spot) in _scenarios)
        {
            RunAccumulation(label, emission, spots[spot]);
        }

        // Сценарии спада.
        RunDecay("decay, player stays (active sector)", active: true);
        RunDecay("decay, player left (absent sector)", active: false);
    }

    /// <summary>Сценарий накопления: излучение в клетке на протяжении HorizonMinutes.</summary>
    /// <param name="label">Метка сценария.</param>
    /// <param name="emission">Излучение источника.</param>
    /// <param name="cell">Клетка источника.</param>
    private static void RunAccumulation(string label, float emission, Vector2I cell)
    {
        SignalGrid grid = MakeGrid();
        GameClock clock = new();
        Vector2 position = Iso.CellToWorld(cell);
        grid.Init();
        GD.Print($"\n--- {label}  (coverage {grid.GetCoverage(cell):F2}, emission {emission:F1}) ---");

        Dictionary<float, int> reached = [];
        StringBuilder row = new();
        for (int minute = 1; minute <= HorizonMinutes; minute++)
        {
            // Пик минуты - после накопления, до спада (пороги в игре ловятся так же).
            grid.Accumulate(position, emission, 1.0f);
            float value = grid.GetNoticeAt(position);
            clock.Advance(GameClock.MinuteDuration);
            foreach (float threshold in _thresholds)
            {
                if (value >= threshold)
                {
                    reached.TryAdd(threshold, minute);
                }
            }
            if (minute % PrintEvery == 0)
            {
                row.Append($"  {minute,2}:{value,5:F1}");
            }
            if (value >= _thresholds[^1])
            {
                break;
            }
        }
        GD.Print(row.ToString());
        GD.Print("  " + Summary(reached));
        grid.Deinit();
    }

    /// <summary>Сценарий спада со 100% до нуля.</summary>
    /// <param name="label">Метка сценария.</param>
    /// <param name="active">Игрок остался в секторе.</param>
    private static void RunDecay(string label, bool active)
    {
        SignalGrid grid = MakeGrid();
        GameClock clock = new();
        Vector2 home = Iso.CellToWorld(Vector2I.Zero);
        Vector2 away = Iso.CellToWorld(new Vector2I(SignalGrid.SectorSize + 1, 0));
        grid.Init();
        grid.Accumulate(home, ChargeEmission, 1000.0f);
        if (!active)
        {
            grid.Accumulate(away, 0.0f, 1.0f);
        }

        int minutes = 0;
        while (grid.GetNotice(Vector2I.Zero) > 0.0f && minutes < 1000)
        {
            clock.Advance(GameClock.MinuteDuration);
            minutes++;
        }
        GD.Print($"\n--- {label} ---\n  100% -> 0% in {minutes} min");
        grid.Deinit();
    }

    /// <summary>Сводка по порогам: минута достижения или never.</summary>
    /// <param name="reached">Минуты достижения по порогам.</param>
    /// <returns>Строка сводки.</returns>
    private static string Summary(Dictionary<float, int> reached)
    {
        List<string> parts = [];
        foreach (float threshold in _thresholds)
        {
            string when = reached.TryGetValue(threshold, out int minute) ? $"{minute} min" : "never";
            parts.Add($"{(int)threshold}% at {when}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>Поиск клетки на оси X с покрытием, ближайшим к цели.</summary>
    /// <param name="grid">Сетка роя.</param>
    /// <param name="target">Целевое покрытие.</param>
    /// <returns>Ближайшая по покрытию клетка.</returns>
    private static Vector2I FindCellWithCoverage(SignalGrid grid, float target)
    {
        Vector2I best = Vector2I.Zero;
        float bestDelta = float.PositiveInfinity;
        for (int x = 0; x < grid.Size.X; x++)
        {
            Vector2I cell = new(x, 0);
            float delta = Math.Abs(grid.GetCoverage(cell) - target);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = cell;
            }
        }
        return best;
    }

    /// <summary>Грид с одной вышкой в (0,0) и игровым радиусом.</summary>
    /// <returns>Готовая сетка.</returns>
    private static SignalGrid MakeGrid()
    {
        SignalGrid grid = new(_origin, _size);
        grid.AddTower(Vector2I.Zero, TowerMarker.DefaultRadius);
        return grid;
    }
}
