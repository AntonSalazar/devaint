using System.Linq;
using DevAInt.Sim;
using DevAInt.Sim.Locale;
using DevAInt.Sim.Net;

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
        _model = new HudModel(ClientTables.From(_game.Sim), _game.Client.Replica);
        _game.Tick();
    }

    /// <summary>Переводы, загруженные тестом, убираются.</summary>
    public override void AfterEach() => GameLocale.Apply(new LocaleTable(), GameLocale.DefaultLanguage);

    /// <summary>Полоса стата с коротким именем, доля от min до max.</summary>
    public void TestBarsFollowOwnStats()
    {
        Entity own = _game.Sim.Entity(_game.Client.Replica.OwnEntityId);
        own.Stats.SetValue(_game.Sim.StatTable.StatIndex("core:hunger"), 25);
        _game.Tick();

        StatBar hunger = _model.Bar("core:hunger");

        CheckEqual("hunger", hunger.Name, "bar name");
        CheckNear(0.25, hunger.Fraction, Tolerance, "hunger fraction");
    }

    /// <summary>Панель core из данных: четыре полосы статов, мудлы и предметы в левом верхнем углу.</summary>
    public void TestCorePanelFromData()
    {
        Hud hud = new();
        hud.Init(_model);

        CheckEqual(4, hud.BarValues.Count, "bar count");
        Check(hud.PanelVisible("core:status"), "status panel is visible");
        CheckEqual("apple x3", hud.ItemLines.Single(), "items row");
        hud.Free();
    }

    /// <summary>Радар zombies: значения от Lua доходят до клиента и подставляются в текст панели.</summary>
    public void TestRadarShowsLuaValue()
    {
        GameFixture game = new("core", "zombies");
        GameLocale.Apply(GameLocale.Load(game.Loaded, new ListLog()), GameLocale.DefaultLanguage);
        HudModel model = new(ClientTables.From(game.Sim), game.Client.Replica);
        Hud hud = new();
        hud.Init(model);
        game.Tick(2);
        hud.Refresh();

        string nearby = game.Client.Replica.UiValues["zombies:nearby"];

        CheckEqual($"Zombies within 10 m: {nearby}", hud.TextLines.Single(), "radar text");
        Check(hud.PanelVisible("zombies:radar"), "radar is visible");
        hud.Free();
    }

    /// <summary>Кнопка радара уходит на сервер, Lua меняет дальность, клиент видит новое значение.</summary>
    public void TestRadarButtonCallsLua()
    {
        GameFixture game = new("core", "zombies");
        HudModel model = new(ClientTables.From(game.Sim), game.Client.Replica);
        Hud hud = new();
        hud.Init(model);
        hud.ActionPressed += game.Client.SendUiAction;
        game.Tick(2);
        string before = game.Client.Replica.UiValues["zombies:range"];

        hud.Press("zombies:radar_range");
        game.Tick(2);

        CheckEqual("10", before, "first range");
        CheckEqual("20", game.Client.Replica.UiValues["zombies:range"], "range after the button");
        hud.Free();
    }

    /// <summary>ui.show прячет панель: ключ show:id со значением false от Lua.</summary>
    public void TestPanelHiddenByShowValue()
    {
        Hud hud = new();
        hud.Init(_model);

        _game.Sim.SetUiValue(_game.Client.Replica.OwnEntityId, "show:core:status", "false");
        _game.Tick();
        hud.Refresh();

        Check(!hud.PanelVisible("core:status"), "status is hidden");
        hud.Free();
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
        // Жажда - вторая строка панели core:status
        CheckNear(0.95, hud.BarValues[1], Tolerance, "thirst bar");
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
