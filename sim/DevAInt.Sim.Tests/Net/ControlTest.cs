using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты действий управления из данных: прицел, удар только при прицеле, предмет, хук Lua.</summary>
public class ControlTest
{
    /// <summary>Пока прицел зажат, взгляд идёт за курсором, а не за движением; отпустил - снова за движением.</summary>
    [Fact]
    public void AimFacesCursor()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        Entity own = run.Sim.Entity(client.Replica.OwnEntityId);
        int aim = run.Sim.ControlTable.Index("core:aim");

        client.SendControl(aim, true);
        client.SendInput(new MoveInput(0, 1), new WorldPos(10, 6.5, 0));
        run.Tick(1, client);
        (double X, _) = own.Facing;
        client.SendControl(aim, false);
        client.SendInput(new MoveInput(0, 1), new WorldPos(10, 6.5, 0));
        run.Tick(1, client);

        Assert.Equal(1, X, 2);
        Assert.Equal((0.0, 1.0), own.Facing);
        Assert.False(own.Aiming);
    }

    /// <summary>Вид от первого лица просит смотреть на точку прицела: взгляд идёт за ней без зажатого прицела, но удар всё так же требует прицела.</summary>
    [Fact]
    public void FaceAimTurnsWithoutAiming()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        Entity own = run.Sim.Entity(client.Replica.OwnEntityId);

        client.SendInput(new MoveInput(0, 1), new WorldPos(own.Position.X + 3, own.Position.Y, 0), faceAim: true);
        run.Tick(1, client);
        (double facedX, _) = own.Facing;
        client.SendInput(new MoveInput(0, 1), new WorldPos(own.Position.X + 3, own.Position.Y, 0));
        run.Tick(1, client);

        Assert.Equal(1, facedX, 2);
        Assert.Equal((0.0, 1.0), own.Facing);
        Assert.False(own.Aiming);
    }

    /// <summary>Удар без прицела не проходит, с прицелом проходит.</summary>
    [Fact]
    public void AttackNeedsAim()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        long dummy = run.Sim.CreateEntity(new WorldPos(6.5, 7.3, 0), "core:human");
        int health = run.Sim.StatTable.StatIndex("core:health");

        client.SendControl(run.Sim.ControlTable.Index("core:attack"), true);
        run.Tick(1, client);
        double unaimed = run.Sim.Entity(dummy).Stats.Value(health);
        run.Strike(client, new WorldPos(6.5, 7.3, 0));
        run.Tick(1, client);

        Assert.Equal(100, unaimed);
        Assert.Equal(75, run.Sim.Entity(dummy).Stats.Value(health));
    }

    /// <summary>E открывает и закрывает дверь впереди; применение предмета на E больше не висит.</summary>
    [Fact]
    public void InteractTogglesDoorAhead()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        Entity own = run.Sim.Entity(client.Replica.OwnEntityId);
        own.Position = new WorldPos(6.5, 8.4, 0);
        own.Facing = (0, 1);
        int interact = run.Sim.ControlTable.Index("core:interact");

        client.SendControl(interact, true);
        run.Tick(1, client);
        bool opened = run.Sim.World.GetEdge(new CellPos(6, 8, 0), Side.South).Open;
        bool clientSees = client.Replica.World!.GetEdge(new CellPos(6, 8, 0), Side.South).Open;
        client.SendControl(interact, true);
        run.Tick(1, client);

        Assert.True(opened, "door opened");
        Assert.True(clientSees, "client sees it open");
        Assert.False(run.Sim.World.GetEdge(new CellPos(6, 8, 0), Side.South).Open, "door closed again");
        Assert.Null(own.Action);
        Assert.DoesNotContain(Enumerable.Range(0, run.Sim.ControlTable.Count), i => run.Sim.ControlTable.Control(i).Id == "core:use_item");
    }

    /// <summary>Мод может повесить применение предмета на свою клавишу: действие ядра useItem осталось.</summary>
    [Fact]
    public void ModControlUsesItem()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("test", depends: "core").Write("test/data/controls.jsonc", """{ "controls": [ { "id": "test:use", "keys": ["key_u"], "does": "useItem" } ] }""");
        ModLoadResult loaded = mods.Load("core", "test");
        Assert.True(loaded.Ok, string.Join("\n", loaded.Errors));
        Simulation sim = new(loaded, new ListLog(), "core:starter_room");
        long anna = sim.SpawnPlayer("anna");

        sim.Control(anna, sim.ControlTable.Index("test:use"), true);

        Assert.Equal("core:eat", sim.Entity(anna).Action?.Action.Id);
    }

    /// <summary>Номер действия вне таблицы - мусорный пакет, подключение разрывается.</summary>
    [Fact]
    public void BadControlDisconnects()
    {
        NetRun run = new();
        IConnection raw = run.ConnectRaw();
        raw.Send(Protocol.Write(new HelloMessage("anna")));
        run.Tick(1);

        raw.Send(Protocol.Write(new ControlMessage(99, true)));
        run.Tick(1);

        Assert.False(raw.IsConnected);
        Assert.Contains("control 99 is out of range", Assert.Single(run.Log.Errors));
    }

    /// <summary>Действие с поведением lua вызывает хук control с id действия и точкой курсора.</summary>
    [Fact]
    public void LuaControlCallsHook()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        mods.Manifest("magic", depends: "core")
            .Write("magic/data/controls.jsonc", """{ "controls": [ { "id": "magic:blink", "keys": ["key_q"], "does": "lua" } ] }""")
            .Write("magic/scripts/main.lua", """
                hooks.add("control", function(entity, control, x, y)
                  if control == "magic:blink" then stats.set(entity, "core:hunger", x * 10) end
                end)
                """);
        ModLoadResult loaded = mods.Load("core", "magic");
        Assert.True(loaded.Ok, string.Join("\n", loaded.Errors));
        Simulation sim = new(loaded, new ListLog(), "core:starter_room");
        Entity anna = sim.Entity(sim.SpawnPlayer("anna"));
        anna.AimPoint = new WorldPos(4.5, 5, 0);

        sim.Control(anna.Id, sim.ControlTable.Index("magic:blink"), true);

        Assert.Equal(45, anna.Stats.Value(sim.StatTable.StatIndex("core:hunger")));
    }
}
