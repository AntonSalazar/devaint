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
        _model = new HudModel(_game.Sim.StatTable, _game.Sim.ItemTable, _game.Client.Replica);
        _game.Tick();
    }

    /// <summary>Полоса на каждый стат core с коротким именем, доля от min до max.</summary>
    public void TestBarsFollowOwnStats()
    {
        Entity own = _game.Sim.Entity(_game.Client.Replica.OwnEntityId);
        own.Stats.SetValue(_game.Sim.StatTable.StatIndex("core:hunger"), 25);
        _game.Tick();

        StatBar hunger = _model.Bars().Single(bar => bar.Name == "hunger");

        CheckEqual(4, _model.Bars().Count, "bar count");
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
        CheckNear(0.95, hud.BarValues[_game.Sim.StatTable.StatIndex("core:thirst")], Tolerance, "thirst bar");
        hud.Free();
    }

    /// <summary>Предметы по видам: игрок появляется с тремя яблоками, после еды их два.</summary>
    public void TestItemsAreGrouped()
    {
        Hud hud = new();
        hud.Init(_model);
        CheckEqual("apple x3", _model.Items().Single(), "start");

        _game.Client.SendStartAction(_game.Client.Replica.Inventory[0].Id, _game.Sim.ItemTable.ActionIndex("core:eat"));
        _game.Tick(42);
        hud.Refresh();

        CheckEqual("apple x2", hud.ItemLines.Single(), "after eating");
        hud.Free();
    }

    /// <summary>Короткое имя из id: пространство имён убирается, подчёркивания - пробелы.</summary>
    public void TestShortName() => CheckEqual("low power", HudModel.ShortName("robots:low_power"), "short name");

    /// <summary>После смерти своего персонажа надпись видна 3 секунды.</summary>
    public void TestDeathMessage()
    {
        Hud hud = new();
        hud.Init(_model);
        Entity own = _game.Sim.Entity(_game.Client.Replica.OwnEntityId);

        own.Stats.SetValue(_game.Sim.StatTable.StatIndex("core:health"), 0);
        _game.Tick();
        hud.Refresh();
        bool shown = hud.DeathVisible;
        hud.Refresh(HudModel.DeathMessageSeconds + 0.1);

        Check(shown, "death message is shown");
        Check(!hud.DeathVisible, "death message is hidden after 3 seconds");
        hud.Free();
    }
}
