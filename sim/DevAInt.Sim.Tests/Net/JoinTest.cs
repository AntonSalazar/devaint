using System;
using System.IO;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Net;
using DevAInt.Sim.Saves;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты входа игроков по id, возврата персонажа и автосохранения сервера.</summary>
public sealed class JoinTest : IDisposable
{
    /// <summary>Временная папка для сохранения.</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devaint-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Папка сохранения.</summary>
    private string SaveDir => Path.Combine(_root, "save");

    /// <summary>Удаление временных файлов.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Игрок вышел и вернулся: тот же персонаж на том же месте с теми же предметами.</summary>
    [Fact]
    public void ReturningPlayerGetsSameCharacter()
    {
        NetRun run = new(save: new SaveGame(SaveDir));
        GameClient anna = run.Connect(run.ConnectRaw(), "anna");
        run.Tick(1, anna);
        long entity = anna.Replica.OwnEntityId;
        anna.SendInput(new MoveInput(1, 0));
        run.Tick(5, anna);
        anna.SendInput(MoveInput.None);
        run.Tick(1, anna);
        WorldPos position = run.Sim.Entity(entity).Position;
        run.Sim.Entity(entity).Inventory.Remove(run.Sim.Entity(entity).Inventory.Items[0].Id);

        run.Server.ClientConnection(0).Disconnect();
        run.Tick(1, anna);
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => run.Sim.Entity(entity));
        Assert.True(File.Exists(Path.Combine(SaveDir, "players", "anna.bin")), "player is saved on leave");

        GameClient again = run.Connect(run.ConnectRaw(), "anna");
        run.Tick(1, again);

        Assert.Equal(entity, again.Replica.OwnEntityId);
        Assert.Equal(position, run.Sim.Entity(entity).Position);
        Assert.Equal(2, again.Replica.Inventory.Count);
        Assert.Contains("player anna joined", run.Log.Infos);
        Assert.Contains("player anna left", run.Log.Infos);
    }

    /// <summary>Новый игрок появляется в точке появления со стартовыми предметами.</summary>
    [Fact]
    public void NewPlayerSpawns()
    {
        NetRun run = new(save: new SaveGame(SaveDir));
        GameClient bob = run.Connect(run.ConnectRaw(), "bob");

        run.Tick(1, bob);

        Assert.Equal("bob", run.Sim.Entity(bob.Replica.OwnEntityId).PlayerId);
        Assert.Equal(3, bob.Replica.Inventory.Count);
    }

    /// <summary>Второе подключение с тем же id отклоняется, первое остаётся.</summary>
    [Fact]
    public void SamePlayerTwiceIsRejected()
    {
        NetRun run = new();
        GameClient first = run.Connect(run.ConnectRaw(), "anna");
        IConnection second = run.ConnectRaw();
        second.Send(Protocol.Write(new HelloMessage("anna")));

        run.Tick(1, first);

        Assert.False(second.IsConnected);
        Assert.True(first.IsConnected);
        Assert.Contains("player anna is already connected", Assert.Single(run.Log.Errors));
        Assert.Equal(1, run.Server.ClientCount);
    }

    /// <summary>Неверный id, сообщение до приветствия и второе приветствие разрывают подключение.</summary>
    [Theory]
    [InlineData("../anna", "player id ../anna is not valid")]
    [InlineData(null, "the first message must be hello")]
    [InlineData("anna,anna", "hello is sent twice")]
    public void BadHelloDisconnects(string? hello, string error)
    {
        NetRun run = new();
        IConnection raw = run.ConnectRaw();
        foreach (string id in (hello ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            raw.Send(Protocol.Write(new HelloMessage(id)));
        }

        raw.Send(Protocol.Write(new InputMessage(1, 1, 0)));
        run.Tick(1);

        Assert.False(raw.IsConnected);
        Assert.Contains(error, Assert.Single(run.Log.Errors));
    }

    /// <summary>Пока клиент не опознан, пакетов он не получает и в мире его нет.</summary>
    [Fact]
    public void ClientWithoutHelloGetsNothing()
    {
        NetRun run = new();
        IConnection raw = run.ConnectRaw();

        run.Tick(3);

        Assert.False(raw.TryReceive(out _));
        Assert.Empty(run.Sim.Entities);
        Assert.Equal(1, run.Server.ClientCount);
    }

    /// <summary>Сервер сам сохраняет мир раз в AutosaveTicks тиков.</summary>
    [Fact]
    public void ServerAutosaves()
    {
        NetRun run = new(save: new SaveGame(SaveDir));
        GameClient anna = run.Connect(run.ConnectRaw(), "anna");

        run.Tick(GameServer.AutosaveTicks - 1, anna);
        bool early = File.Exists(Path.Combine(SaveDir, "meta.json"));
        run.Tick(1, anna);

        Assert.False(early);
        Assert.True(File.Exists(Path.Combine(SaveDir, "meta.json")), "meta after autosave");
        Assert.True(File.Exists(Path.Combine(SaveDir, "players", "anna.bin")), "online player after autosave");
    }

    /// <summary>Ошибка записи на диск пишется в лог, сервер работает дальше.</summary>
    [Fact]
    public void SaveFailureIsLogged()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SaveDir, "a file where the save folder should be");
        NetRun run = new(save: new SaveGame(SaveDir));

        run.Server.Save();
        run.Tick(1);

        Assert.StartsWith($"save to {SaveDir} failed", Assert.Single(run.Log.Errors));
    }

    /// <summary>Без сохранения в папке - новый мир на карте, с сохранением - загруженный.</summary>
    [Fact]
    public void LoadOrCreate()
    {
        using TempMods mods = new();
        mods.CopyRepoMod("core");
        SaveGame save = new(SaveDir);

        Simulation fresh = save.LoadOrCreate(mods.Load("core"), new Lua.ListLog(), "core:starter_room", timeScale: 2);
        fresh.Tick();
        save.Save(fresh);
        Simulation loaded = new SaveGame(SaveDir).LoadOrCreate(mods.Load("core"), new Lua.ListLog(), "core:starter_room");

        Assert.Equal(2, fresh.Clock.TimeScale);
        Assert.Equal(1, loaded.Clock.Tick);
        Assert.DoesNotContain(loaded.Entities, entity => entity.PlayerId is not null);
    }
}
