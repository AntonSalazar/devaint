using System.Linq;
using DevAInt.Sim;

/// <summary>Тесты <see cref="HudModel"/> и <see cref="Hud"/> на настоящих сервере и клиенте.</summary>
public class HudTest : TestCase
{
    /// <summary>Допуск долей полос.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>Сервер и клиент.</summary>
    private GameFixture _game = null!;

    /// <summary>Модель HUD.</summary>
    private HudModel _model = null!;

    /// <summary>Новая игра и модель перед каждым тестом.</summary>
    public override void BeforeEach()
    {
        _game = new GameFixture();
        _model = new HudModel(_game.Sim.StatTable, _game.Client.Replica);
        _game.Tick();
    }

    /// <summary>Полоса на каждый стат core с коротким именем, доля от min до max.</summary>
    public void TestBarsFollowOwnStats()
    {
        Entity own = _game.Sim.Entity(_game.Client.Replica.OwnEntityId);
        own.Stats.SetValue(_game.Sim.StatTable.StatIndex("core:hunger"), 25);
        _game.Tick();

        StatBar hunger = _model.Bars().Single(bar => bar.Name == "hunger");

        CheckEqual(3, _model.Bars().Count, "bar count");
        CheckNear(0.25, hunger.Fraction, Tolerance, "hunger fraction");
    }

    /// <summary>Итог этапа 8: на пороге голода в HUD появляется мудл, на втором пороге - его второй уровень.</summary>
    public void TestMoodleAppearsAtThreshold()
    {
        Entity own = _game.Sim.Entity(_game.Client.Replica.OwnEntityId);
        int hunger = _game.Sim.StatTable.StatIndex("core:hunger");
        CheckEqual(0, _model.Moodles().Count, "no moodles at start");

        own.Stats.SetValue(hunger, 45);
        _game.Tick();
        CheckEqual("hungry I", _model.Moodles().Single(), "first level");

        own.Stats.SetValue(hunger, 75);
        _game.Tick();
        CheckEqual("hungry II", _model.Moodles().Single(), "second level");
    }

    /// <summary>Узел HUD показывает полосы и строки мудлов модели после Refresh.</summary>
    public void TestHudNodeShowsModel()
    {
        Hud hud = new();
        hud.Init(_model);
        Entity own = _game.Sim.Entity(_game.Client.Replica.OwnEntityId);
        own.Stats.SetValue(_game.Sim.StatTable.StatIndex("core:thirst"), 95);
        _game.Tick();

        hud.Refresh();

        CheckEqual("thirsty III", hud.MoodleLines.Single(), "moodle line");
        CheckNear(0.95, hud.BarValues.Max(), Tolerance, "thirst bar");
        hud.Free();
    }

    /// <summary>Короткое имя из id: пространство имён убирается, подчёркивания - пробелы.</summary>
    public void TestShortName() => CheckEqual("low power", HudModel.ShortName("robots:low_power"), "short name");
}
