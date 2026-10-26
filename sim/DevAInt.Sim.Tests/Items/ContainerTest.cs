using System.IO;
using System.Linq;
using DevAInt.Sim.Defs;
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

/// <summary>Тесты контейнеров: стартовые предметы, досягаемость, перекладывание с проверками и подписка по сети.</summary>
public class ContainerTest
{
    /// <summary>Клетка ящика стартовой комнаты.</summary>
    private static readonly CellPos _crateCell = new(9, 8, 0);

    /// <summary>Ящик стартовой комнаты получает свои яблоки, вес считается по предметам.</summary>
    [Fact]
    public void MapFillsCrate()
    {
        Simulation sim = StarterRoom();

        Inventory crate = sim.World.Container(Crate(sim).Id)!;

        Assert.Equal(["core:apple", "core:apple", "core:log", "core:log"], crate.Items.Select(item => item.Def.Id));
        Assert.Equal(8.4, crate.Weight, 9);
        Assert.Null(sim.World.Container(sim.World.ObjectAt(new CellPos(4, 4, 0))!.Id));
    }

    /// <summary>Рукой достаётся вплотную, но не издалека, не через стену и не с другого этажа.</summary>
    [Fact]
    public void ReachNeedsDistanceAndNoWall()
    {
        Simulation sim = StarterRoom();
        WorldObject crate = Crate(sim);

        Assert.True(Reach.CanReach(sim.World, new WorldPos(8.5, 8.5, 0), crate), "next to the crate");
        Assert.False(Reach.CanReach(sim.World, new WorldPos(6.5, 6.5, 0), crate), "across the room");
        Assert.False(Reach.CanReach(sim.World, new WorldPos(9.5, 9.4, 0), crate), "behind the south wall");
        Assert.False(Reach.CanReach(sim.World, new WorldPos(8.5, 8.5, 1), crate), "another floor");
    }

    /// <summary>Яблоко из ящика в руки и обратно; каждая причина отказа своя.</summary>
    [Fact]
    public void MoveItemChecksEverything()
    {
        Simulation sim = StarterRoom();
        WorldObject crate = Crate(sim);
        long entity = sim.CreateEntity(new WorldPos(8.5, 8.5, 0), "core:human");
        Inventory box = sim.World.Container(crate.Id)!;
        long apple = box.Items[0].Id;
        long table = sim.World.ObjectAt(new CellPos(4, 4, 0))!.Id;

        Assert.Equal(ItemMove.Moved, sim.MoveItem(entity, apple, crate.Id, 0));
        Assert.Contains(sim.Entity(entity).Inventory.Items, item => item.Id == apple);
        Assert.Equal(ItemMove.NoItem, sim.MoveItem(entity, apple, crate.Id, 0));
        Assert.Equal(ItemMove.SamePlace, sim.MoveItem(entity, apple, 0, 0));
        Assert.Equal(ItemMove.NoContainer, sim.MoveItem(entity, apple, 0, table));
        Assert.Equal(ItemMove.NoContainer, sim.MoveItem(entity, apple, 0, 999));

        sim.StartAction(entity, apple, "core:eat");
        Assert.Equal(ItemMove.InUse, sim.MoveItem(entity, apple, 0, crate.Id));
        sim.CancelAction(entity);
        Assert.Equal(ItemMove.Moved, sim.MoveItem(entity, apple, 0, crate.Id));

        sim.Entity(entity).Position = new WorldPos(6.5, 6.5, 0);
        Assert.Equal(ItemMove.OutOfReach, sim.MoveItem(entity, apple, crate.Id, 0));
    }

    /// <summary>Вместимость: человек несёт не больше 15 кг, ровно 15 кг ещё влезает.</summary>
    [Fact]
    public void CapacityIsKept()
    {
        Simulation sim = StarterRoom();
        WorldObject crate = Crate(sim);
        long entity = sim.CreateEntity(new WorldPos(8.5, 8.5, 0), "core:human");
        ItemDef apple = sim.ItemTable.Item(sim.ItemTable.ItemIndex("core:apple"));
        for (int i = 0; i < 74; i++)
        {
            sim.GiveItem(entity, apple);
        }

        Inventory box = sim.World.Container(crate.Id)!;
        ItemMove last = sim.MoveItem(entity, box.Items[0].Id, crate.Id, 0);
        ItemMove over = sim.MoveItem(entity, box.Items[0].Id, crate.Id, 0);

        Assert.Equal(ItemMove.Moved, last);
        Assert.Equal(ItemMove.Full, over);
        Assert.Equal(15, sim.Entity(entity).Inventory.Weight, 9);
    }

    /// <summary>Подписка: содержимое приходит, после перекладывания обновляется, а отошедшему - закрывается.</summary>
    [Fact]
    public void WatchAndMoveOverNetwork()
    {
        NetRun run = new();
        GameClient client = run.Connect();
        run.Tick(1, client);
        Entity own = run.Sim.Entity(client.Replica.OwnEntityId);
        own.Position = new WorldPos(8.5, 8.5, 0);
        long crate = Crate(run.Sim).Id;
        int changes = 0;
        client.Replica.ContainersChanged += () => changes++;

        client.SendWatchContainers([crate]);
        run.Tick(1, client);
        int seen = client.Replica.Containers[crate].Count;
        client.SendMoveItem(client.Replica.Containers[crate][0].Id, crate, 0);
        run.Tick(1, client);
        int left = client.Replica.Containers[crate].Count;
        int ownItems = client.Replica.Inventory.Count;
        own.Position = new WorldPos(6.5, 6.5, 0);
        run.Tick(1, client);

        Assert.Equal(4, seen);
        Assert.Equal(3, left);
        Assert.Equal(4, ownItems);
        Assert.False(client.Replica.Containers.ContainsKey(crate), "closed when out of reach");
        Assert.Equal(3, changes);
    }

    /// <summary>Содержимое контейнера сохраняется и загружается; вид, переставший быть контейнером, теряет предметы с записью в лог.</summary>
    [Fact]
    public void ContainerSurvivesSave()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        ModLoadResult loaded = mods.Load("core");
        ListLog log = new();
        Simulation sim = new(loaded, log, "core:starter_room");
        string dir = Path.Combine(Path.GetDirectoryName(mods.Root)!, "save");
        new SaveGame(dir).Save(sim);

        Simulation same = new SaveGame(dir).Load(mods.Load("core"), log);
        mods.Write("core/data/objects.jsonc", File.ReadAllText(Path.Combine(mods.Root, "core", "data", "objects.jsonc"))
            .Replace("""{ "id": "core:crate", "height": 0.6, "color": "#9a7b4f", "container": { "capacity": 20 },""", """{ "id": "core:crate", "height": 0.6, "color": "#9a7b4f","""));
        Simulation changed = new SaveGame(dir).Load(mods.Load("core"), log);

        Assert.Equal(Items(sim), Items(same));
        Assert.Null(changed.World.Container(Crate(changed).Id));
        Assert.Equal(4, log.Infos.Count(line => line.Contains("is not a container any more")));
    }

    /// <summary>Id и виды предметов ящика.</summary>
    private static string[] Items(Simulation sim) => [.. sim.World.Container(Crate(sim).Id)!.Items.Select(item => $"{item.Id} {item.Def.Id}")];

    /// <summary>Ящик стартовой комнаты.</summary>
    private static WorldObject Crate(Simulation sim) => sim.World.ObjectAt(_crateCell)!;

    /// <summary>Стартовая комната core.</summary>
    private static Simulation StarterRoom() => new(ModLoader.Load(RepoMods.Root, ["core"]), new ListLog(), "core:starter_room");
}
