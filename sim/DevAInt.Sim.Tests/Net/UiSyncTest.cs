using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты интерфейса модов по сети: значения от Lua доходят до своих клиентов только при изменении, кнопка вызывает хук.</summary>
public class UiSyncTest
{
    /// <summary>Панель мода alpha с одной кнопкой.</summary>
    private const string Panel = """{ "ui": [ { "id": "alpha:panel", "rows": [ { "text": "alpha:hint" }, { "button": "alpha:press", "action": "alpha:press" } ] } ] }""";

    /// <summary>Своё значение у каждого игрока и общее у всех, хотя Lua ставит их каждый тик, приходят по одному разу; рядом меняющийся счётчик приходит каждый тик.</summary>
    [Fact]
    public void ValuesReachPlayersOnlyOnChange()
    {
        Run run = Run.With("""
            hooks.add("tick", function()
              for _, player in ipairs(entities.players()) do
                ui.set(player.id, "alpha:me", player.id)
              end
              ui.set(nil, "alpha:all", "hi")
              ticks = (ticks or 0) + 1
              ui.set(nil, "alpha:ticks", ticks)
              ui.set(nil, "alpha:players", #entities.players())
            end)
            """);
        GameClient anna = run.Connect("anna");
        GameClient bob = run.Connect("bob");
        run.Sim.CreateEntity(new WorldPos(6.5, 6.5, 0), "core:human");
        Dictionary<string, int> changes = [];
        anna.Replica.UiValueChanged += key => changes[key] = changes.GetValueOrDefault(key) + 1;

        run.Tick(10, anna, bob);

        Assert.Equal(anna.Replica.OwnEntityId.ToString(), anna.Replica.UiValues["alpha:me"]);
        Assert.Equal(bob.Replica.OwnEntityId.ToString(), bob.Replica.UiValues["alpha:me"]);
        Assert.Equal("hi", bob.Replica.UiValues["alpha:all"]);
        Assert.Equal("2", bob.Replica.UiValues["alpha:players"]);
        Assert.Equal((1, 1), (changes["alpha:me"], changes["alpha:all"]));
        Assert.True(changes["alpha:ticks"] >= 9, $"ticks changed {changes["alpha:ticks"]} times");
    }

    /// <summary>Своё значение закрывает общее с тем же ключом; убранное своё открывает общее, убранное общее исчезает у клиента.</summary>
    [Fact]
    public void OwnValueOverridesGlobal()
    {
        Run run = Run.With(string.Empty);
        GameClient client = run.Connect("anna");
        run.Tick(1, client);
        long own = client.Replica.OwnEntityId;

        run.Sim.SetUiValue(null, "alpha:k", "all");
        run.Sim.SetUiValue(own, "alpha:k", "mine");
        run.Tick(1, client);
        string overridden = client.Replica.UiValues["alpha:k"];
        run.Sim.SetUiValue(own, "alpha:k", null);
        run.Tick(1, client);
        string uncovered = client.Replica.UiValues["alpha:k"];
        run.Sim.SetUiValue(null, "alpha:k", null);
        run.Tick(1, client);

        Assert.Equal(("mine", "all"), (overridden, uncovered));
        Assert.Equal((2, 2), (run.Sim.Entity(own).UiVersion, run.Sim.GlobalUiVersion));
        Assert.False(client.Replica.UiValues.ContainsKey("alpha:k"));
    }

    /// <summary>После смерти значения прежнего персонажа у клиента пропадают, а значение нового приходит, даже если номер изменения у них один.</summary>
    [Fact]
    public void OwnValuesResetAfterDeath()
    {
        Run run = Run.With(string.Empty);
        GameClient client = run.Connect("anna");
        run.Tick(1, client);
        long first = client.Replica.OwnEntityId;
        run.Sim.SetUiValue(first, "alpha:k", "old");
        run.Tick(1, client);
        bool had = client.Replica.UiValues.ContainsKey("alpha:k");

        run.Sim.Entity(first).Stats.SetValue(run.Sim.StatTable.StatIndex("core:health"), 0);
        run.Tick(3, client);
        run.Sim.SetUiValue(client.Replica.OwnEntityId, "alpha:k2", "new");
        run.Tick(1, client);

        Assert.True(had, "value before death");
        Assert.NotEqual(first, client.Replica.OwnEntityId);
        Assert.False(client.Replica.UiValues.ContainsKey("alpha:k"));
        Assert.Equal("new", client.Replica.UiValues["alpha:k2"]);
    }

    /// <summary>Кнопка клиента вызывает хук ui_action со своей сущностью и id действия; ui.show прячет панель ключом show.</summary>
    [Fact]
    public void ButtonCallsHook()
    {
        Run run = Run.With("""
            hooks.add("ui_action", function(entity, action)
              print(entity .. " " .. action)
              ui.show(entity, "alpha:panel", false)
            end)
            """);
        GameClient client = run.Connect("anna");
        run.Tick(1, client);
        Assert.True(run.Sim.UiTable.TryActionIndex("alpha:press", out int press));

        client.SendUiAction(press);
        run.Tick(2, client);

        Assert.Contains($"[alpha] {client.Replica.OwnEntityId} alpha:press", run.Log.Infos);
        Assert.Equal("false", client.Replica.UiValues["show:alpha:panel"]);
    }

    /// <summary>Номер действия за краем таблицы - ошибка протокола, клиент отключён.</summary>
    [Fact]
    public void UnknownActionIsRejected()
    {
        Run run = Run.With(string.Empty);
        GameClient client = run.Connect("anna");
        run.Tick(1, client);

        client.SendUiAction(99);
        run.Tick(1, client);

        Assert.Contains("ui action 99 is out of range", Assert.Single(run.Log.Errors));
    }

    /// <summary>Значение не того типа и слишком длинное - ошибка Lua с понятным текстом, значение не ставится.</summary>
    [Theory]
    [InlineData("""ui.set(nil, "alpha:k", {})""", "value must be a number, a string or a boolean")]
    [InlineData("""ui.set(nil, "alpha:k", string.rep("x", 2000))""", "value at most 1024 bytes")]
    [InlineData("""ui.set(12345, "alpha:k", 1)""", "unknown entity 12345")]
    public void BadValueIsLuaError(string call, string message)
    {
        Run run = Run.With($$"""
            hooks.add("tick", function()
              {{call}}
            end)
            """);

        run.Sim.Tick();

        Assert.Contains(message, Assert.Single(run.Log.Errors));
        Assert.Empty(run.Sim.GlobalUi);
    }

    /// <summary>Радар zombies считает зомби на этаже игрока в своей дальности, кнопка переключает 10, 20 и 40 метров.</summary>
    [Fact]
    public void ZombieRadarCountsNearby()
    {
        Run run = new(ModLoader.Load(RepoMods.Root, ["core", "zombies"]));
        GameClient client = run.Connect("anna");
        run.Tick(2, client);
        Entity own = run.Sim.Entity(client.Replica.OwnEntityId);
        Assert.True(run.Sim.UiTable.TryActionIndex("zombies:radar_range", out int range));
        List<string> ranges = [client.Replica.UiValues["zombies:range"]];
        List<string> counts = [];
        List<string> expected = [];

        for (int i = 0; i < 3; i++)
        {
            int radius = int.Parse(ranges[^1]);
            expected.Add(run.Sim.Entities.Count(entity => entity.Creature?.Id == "zombies:zombie" && entity.Position.Z == own.Position.Z
                && Distance(entity, own) <= radius).ToString());
            counts.Add(client.Replica.UiValues["zombies:nearby"]);
            client.SendUiAction(range);
            run.Tick(1, client);
            ranges.Add(client.Replica.UiValues["zombies:range"]);
        }

        Assert.Equal(["10", "20", "40", "10"], ranges);
        Assert.Equal(expected, counts);
        Assert.Equal("3", counts[^1]);
    }

    /// <summary>Расстояние между сущностями по полу.</summary>
    private static double Distance(Entity a, Entity b) => System.Math.Sqrt(System.Math.Pow(a.Position.X - b.Position.X, 2) + System.Math.Pow(a.Position.Y - b.Position.Y, 2));

    /// <summary>Сервер со стартовой комнатой и клиенты к нему.</summary>
    private sealed class Run
    {
        /// <summary>Транспорт в памяти.</summary>
        private readonly LoopbackTransport _transport = new();

        /// <summary>Сервер на готовых модах.</summary>
        public Run(ModLoadResult mods)
        {
            Assert.True(mods.Ok, string.Join("\n", mods.Errors));
            Sim = new Simulation(mods, Log, "core:starter_room");
            Assert.Empty(Sim.ScriptErrors);
            Server = new GameServer(Sim, _transport, Log);
        }

        /// <summary>Лог сервера и Lua.</summary>
        public ListLog Log { get; } = new();

        /// <summary>Sim сервера.</summary>
        public Simulation Sim { get; }

        /// <summary>Сервер.</summary>
        public GameServer Server { get; }

        /// <summary>Сервер на core и моде alpha со скриптом script и панелью Panel; папка модов удаляется, когда скрипты уже загружены.</summary>
        public static Run With(string script)
        {
            using TempMods mods = new();
            mods.CopyRepoMod("core");
            mods.Manifest("alpha", depends: "core").Write("alpha/data/ui.jsonc", Panel);
            if (script.Length > 0)
            {
                mods.Write("alpha/scripts/main.lua", script);
            }

            return new Run(mods.Load("core", "alpha"));
        }

        /// <summary>Клиент игрока playerId.</summary>
        public GameClient Connect(string playerId) => new(_transport.Connect(), ClientTables.From(Sim), playerId);

        /// <summary>Тики сервера, после каждого клиенты применяют пакеты.</summary>
        public void Tick(int count, params GameClient[] clients)
        {
            for (int i = 0; i < count; i++)
            {
                Server.Tick();
                foreach (GameClient client in clients)
                {
                    client.Update();
                }
            }
        }
    }
}
