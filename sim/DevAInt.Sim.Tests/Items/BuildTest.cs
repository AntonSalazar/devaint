using System.IO;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;
using DevAInt.Sim.Saves;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using DevAInt.Sim.Tests.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Items;

/// <summary>Тесты строительства: постройка объекта и стены, разбор с возвратом половины, отказы, прерывание, сохранение и сеть.</summary>
public class BuildTest
{
    /// <summary>Где стоит строитель: посреди стартовой комнаты.</summary>
    private static readonly WorldPos _middle = new(6.5, 6.5, 0);

    /// <summary>Ящик в соседней клетке: через 8 секунд он стоит, четыре доски и четыре гвоздя потрачены, молоток остался.</summary>
    [Fact]
    public void BuildCrate()
    {
        (Simulation sim, long builder) = Builder();

        ActionStart start = sim.StartBuild(builder, Crate(sim, 7, 6));
        int ticks = TicksUntilDone(sim, builder);

        Assert.Equal(ActionStart.Started, start);
        Assert.Equal(80, ticks);
        Assert.Equal("core:crate", sim.WorldTable.Object(sim.World.ObjectAt(new CellPos(7, 6, 0))!.Def).Id);
        Assert.Equal(["core:hammer", "core:nails", "core:nails", "core:nails", "core:nails", "core:plank"], Items(sim, builder));
    }

    /// <summary>Стена на южном ребре своей клетки встаёт через 10 секунд и загораживает путь.</summary>
    [Fact]
    public void BuildWall()
    {
        (Simulation sim, long builder) = Builder();
        BuildTarget wall = new(BuildKind.Wall, sim.WorldTable.WallIndex("core:wood_wall"), new CellPos(6, 6, 0), (int)Side.South);

        sim.StartBuild(builder, wall);
        TicksUntilDone(sim, builder);

        Assert.Equal(sim.WorldTable.WallIndex("core:wood_wall"), sim.World.GetEdge(new CellPos(6, 6, 0), Side.South).Wall);
        Assert.False(sim.World.CanCross(new CellPos(6, 6, 0), Side.South));
    }

    /// <summary>Каждая причина отказа своя и понятна.</summary>
    [Fact]
    public void BuildRefusals()
    {
        (Simulation sim, long builder) = Builder();
        (Simulation empty, long poor) = Builder(withMaterials: false);
        WorldPos at = sim.Entity(builder).Position;
        (WorldPos, double)[] bodies = [(at, 0.3)];
        BuildTarget roomWall = new(BuildKind.Wall, sim.WorldTable.WallIndex("core:wood_wall"), new CellPos(6, 4, 0), (int)Side.North);
        BuildTarget mapCrate = new(BuildKind.RemoveObject, 0, new CellPos(9, 8, 0), 0);

        Assert.Equal("someone stands there", Building.Problem(sim.World, sim.WorldTable, Crate(sim, 6, 6), at, bodies));
        Assert.Equal("out of reach", Building.Problem(sim.World, sim.WorldTable, Crate(sim, 2, 2), at, bodies));
        Assert.Equal("the edge already has a wall", Building.Problem(sim.World, sim.WorldTable, roomWall, new WorldPos(6.5, 4.5, 0), []));
        Assert.Equal("the container is not empty", Building.Problem(sim.World, sim.WorldTable, mapCrate, new WorldPos(8.5, 8.5, 0), []));
        Assert.Equal("this cannot be built or removed", Building.Problem(sim.World, sim.WorldTable, mapCrate with { Cell = new CellPos(4, 4, 0) }, new WorldPos(4.5, 5.5, 0), []));
        Assert.Equal("cell 9,4,0 is taken by another object", Building.Problem(sim.World, sim.WorldTable, Crate(sim, 9, 4), new WorldPos(8.5, 5.5, 0), []));
        Assert.Equal(ActionStart.NoItem, empty.StartBuild(poor, Crate(empty, 7, 6)));
        Assert.Equal(ActionStart.BadPlace, sim.StartBuild(builder, Crate(sim, 6, 6)));
        Assert.Equal(ActionStart.Started, sim.StartBuild(builder, Crate(sim, 7, 6)));
        Assert.Equal(ActionStart.Busy, sim.StartBuild(builder, Crate(sim, 5, 6)));
    }

    /// <summary>Разбор своего ящика и стены комнаты возвращает половину материалов с округлением вниз.</summary>
    [Fact]
    public void RemoveGivesHalfBack()
    {
        (Simulation sim, long builder) = Builder();
        sim.StartBuild(builder, Crate(sim, 7, 6));
        TicksUntilDone(sim, builder);

        sim.StartBuild(builder, new BuildTarget(BuildKind.RemoveObject, 0, new CellPos(7, 6, 0), 0));
        TicksUntilDone(sim, builder);
        sim.Entity(builder).Position = new WorldPos(6.5, 4.5, 0);
        sim.StartBuild(builder, new BuildTarget(BuildKind.RemoveWall, 0, new CellPos(6, 4, 0), (int)Side.North));
        TicksUntilDone(sim, builder);

        Assert.Null(sim.World.ObjectAt(new CellPos(7, 6, 0)));
        Assert.Equal(0, sim.World.GetEdge(new CellPos(6, 4, 0), Side.North).Wall);
        Assert.Equal(1 + 2 + 1, Items(sim, builder).Count(item => item == "core:plank"));
        Assert.Equal(4 + 2 + 2, Items(sim, builder).Count(item => item == "core:nails"));
    }

    /// <summary>Пока строилось, на место встал другой: постройка прервана, материалы целы.</summary>
    [Fact]
    public void TakenPlaceInterrupts()
    {
        (Simulation sim, long builder) = Builder();
        sim.StartBuild(builder, Crate(sim, 7, 6));
        sim.CreateEntity(new WorldPos(7.5, 6.5, 0), "core:human");

        TicksUntilDone(sim, builder);

        Assert.Equal(ActionEnd.Interrupted, sim.Entity(builder).LastActionEnd);
        Assert.Null(sim.World.ObjectAt(new CellPos(7, 6, 0)));
        Assert.Equal(5, Items(sim, builder).Count(item => item == "core:plank"));
    }

    /// <summary>Постройка, начатая до сохранения, после загрузки доводится до конца.</summary>
    [Fact]
    public void BuildSurvivesSave()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        Simulation sim = new(mods.Load("core"), new ListLog(), "core:starter_room");
        long builder = sim.CreateEntity(_middle, "core:human");
        GiveMaterials(sim, builder);
        sim.StartBuild(builder, Crate(sim, 7, 6));
        sim.Tick();
        string dir = Path.Combine(Path.GetDirectoryName(mods.Root)!, "save");
        new SaveGame(dir).Save(sim);

        Simulation restored = new SaveGame(dir).Load(mods.Load("core"), new ListLog());
        BuildTarget? target = restored.Entity(builder).Action?.Build;
        TicksUntilDone(restored, builder);

        Assert.Equal(Crate(sim, 7, 6), target);
        Assert.NotNull(restored.World.ObjectAt(new CellPos(7, 6, 0)));
    }

    /// <summary>По сети: клиент просит постройку, через 8 секунд ящик есть и в его мире.</summary>
    [Fact]
    public void BuildOverNetwork()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        GiveMaterials(run.Sim, client.Replica.OwnEntityId);

        client.SendBuild(Crate(run.Sim, 7, 6));
        run.Tick(82, client);

        Assert.NotNull(client.Replica.World!.ObjectAt(new CellPos(7, 6, 0)));
    }

    /// <summary>Подпись действия у клиента: съесть яблоко, построить ящик, разобрать ящик - id что делается и над чем.</summary>
    [Fact]
    public void ActionLabelsOverNetwork()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        GiveMaterials(run.Sim, client.Replica.OwnEntityId);
        ReplicaEntity own = client.Replica.Entity(client.Replica.OwnEntityId)!;

        client.SendStartAction(client.Replica.Inventory[0].Id, run.Sim.ItemTable.ActionIndex("core:eat"));
        run.Tick(1, client);
        (string, string) eat = (own.Action!.Verb, own.Action.Subject);
        client.SendCancelAction();
        client.SendBuild(Crate(run.Sim, 7, 6));
        run.Tick(1, client);
        (string, string) build = (own.Action!.Verb, own.Action.Subject);
        run.Tick(81, client);
        client.SendBuild(new BuildTarget(BuildKind.RemoveObject, 0, new CellPos(7, 6, 0), 0));
        run.Tick(1, client);
        (string, string) remove = (own.Action!.Verb, own.Action.Subject);

        Assert.Equal(("core:eat", "core:apple"), eat);
        Assert.Equal(("core:build", "core:crate"), build);
        Assert.Equal((GameServer.RemoveVerb, "core:crate"), remove);
    }

    /// <summary>Цель: ящик в клетке x, y первого этажа.</summary>
    private static BuildTarget Crate(Simulation sim, int x, int y) => new(BuildKind.Object, sim.WorldTable.ObjectIndex("core:crate"), new CellPos(x, y, 0), 0);

    /// <summary>Тики до конца действия сущности, не больше 500.</summary>
    private static int TicksUntilDone(Simulation sim, long entity)
    {
        int ticks = 0;
        while (sim.Entity(entity).Action is not null && ticks < 500)
        {
            sim.Tick();
            ticks++;
        }

        return ticks;
    }

    /// <summary>Id видов предметов инвентаря по порядку.</summary>
    private static string[] Items(Simulation sim, long entity) => [.. sim.Entity(entity).Inventory.Items.Select(item => item.Def.Id).Order()];

    /// <summary>Пять досок, восемь гвоздей и молоток.</summary>
    private static void GiveMaterials(Simulation sim, long entity)
    {
        string[] items = ["core:hammer", .. Enumerable.Repeat("core:plank", 5), .. Enumerable.Repeat("core:nails", 8)];
        foreach (string item in items)
        {
            sim.GiveItem(entity, sim.ItemTable.Item(sim.ItemTable.ItemIndex(item)));
        }
    }

    /// <summary>Строитель посреди стартовой комнаты, с материалами или без.</summary>
    private static (Simulation Sim, long Builder) Builder(bool withMaterials = true)
    {
        Simulation sim = new(ModLoader.Load(RepoMods.Root, ["core"]), new ListLog(), "core:starter_room");
        long builder = sim.CreateEntity(_middle, "core:human");
        if (withMaterials)
        {
            GiveMaterials(sim, builder);
        }

        return (sim, builder);
    }
}
