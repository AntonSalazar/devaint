using System;
using System.IO;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Saves;
using DevAInt.Sim.Tests.Lua;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Saves;

/// <summary>Тесты <see cref="SaveGame"/>: сохранил, загрузил, совпало; таблица id; повреждённые файлы.</summary>
public sealed class SaveGameTest : IDisposable
{
    /// <summary>Мод с полосой 300 на 8 клеток через два региона, каменным полом и статом маны.</summary>
    private const string TestData = """
        {
          "floors": [ { "id": "test:stone", "color": "#777777" } ],
          "stats": [ { "id": "test:mana", "min": 0, "max": 10, "rate": 0.5 } ],
          "maps": [ { "id": "test:strip", "width": 300, "height": 8, "spawn": { "x": 5, "y": 4, "items": ["core:apple"] },
            "build": [ { "floor": "core:wood_floor", "x": 0, "y": 0, "w": 300, "h": 8 },
                       { "walls": "core:wood_wall", "x": 2, "y": 2, "w": 4, "h": 4 },
                       { "door": "core:wood_door", "x": 3, "y": 5, "side": "south" } ] } ]
        }
        """;

    /// <summary>Временные моды, папка сохранения лежит рядом.</summary>
    private readonly TempMods _mods = new();

    /// <summary>Лог.</summary>
    private readonly ListLog _log = new();

    /// <summary>Моды core и test в папке.</summary>
    public SaveGameTest()
    {
        _mods.CopyRepoMod("core");
        _mods.Manifest("test").Write("test/data/test.jsonc", TestData);
    }

    /// <summary>Папка сохранения.</summary>
    private string SaveDir => Path.Combine(Path.GetDirectoryName(_mods.Root)!, "save");

    /// <summary>Удаление временных файлов.</summary>
    public void Dispose() => _mods.Dispose();

    /// <summary>Итог шага: мир, сущности, время и счётчики после загрузки те же, и дальше игра идёт так же.</summary>
    [Fact]
    public void SaveLoadRoundTrip()
    {
        Simulation original = NewSim();
        long npc = original.CreateEntity(new WorldPos(150.5, 4.5, 0));
        long player = original.SpawnPlayer("anna");
        Entity anna = original.Entity(player);
        original.StartAction(player, anna.Inventory.Items[0].Id, "core:eat");
        original.Entity(npc).Stats.SetValue(original.StatTable.StatIndex("core:hunger"), 75);
        original.Entity(npc).Input = new MoveInput(1, 0);
        CellPos door = new(3, 5, 0);
        original.World.SetEdge(door, Side.South, original.World.GetEdge(door, Side.South) with { Open = true });
        Tick(original, 12);

        new SaveGame(SaveDir).Save(original);
        SaveGame save = new(SaveDir);
        Simulation loaded = save.Load(Load("core", "test"), _log);
        Assert.Equal(player, save.LoadPlayer(loaded, "anna", _log));

        AssertSame(original, loaded);

        // Ввод не сохраняется: после загрузки сущность стоит, поэтому ввод задаётся обоим заново
        loaded.Entity(npc).Input = original.Entity(npc).Input;
        Tick(original, 60);
        Tick(loaded, 60);
        AssertSame(original, loaded);
        Assert.Empty(anna.Inventory.Items);
    }

    /// <summary>Новый мод сдвигает номера полов, но мир по id тот же.</summary>
    [Fact]
    public void NewModDoesNotBreakNumbers()
    {
        Simulation original = NewSim();
        original.World.SetFloor(new CellPos(10, 1, 0), original.WorldTable.FloorIndex("test:stone"));
        new SaveGame(SaveDir).Save(original);
        _mods.Manifest("extra").Write("extra/data/extra.jsonc", """{ "floors": [ { "id": "extra:sand", "color": "#eeddaa" } ] }""");

        Simulation loaded = new SaveGame(SaveDir).Load(Load("core", "extra", "test"), _log);

        Assert.NotEqual(original.WorldTable.FloorIndex("test:stone"), loaded.WorldTable.FloorIndex("test:stone"));
        AssertSameWorld(original, loaded);
    }

    /// <summary>Сохранение с полом, которого нет в модах, не грузится, и файлы остаются прежними.</summary>
    [Fact]
    public void MissingFloorFailsWithoutTouchingFiles()
    {
        Simulation original = NewSim();
        original.World.SetFloor(new CellPos(10, 1, 0), original.WorldTable.FloorIndex("test:stone"));
        new SaveGame(SaveDir).Save(original);
        string before = Snapshot();
        _mods.Write("test/data/test.jsonc", TestData.Replace("test:stone", "test:marble"));

        SaveException error = Assert.Throws<SaveException>(() => new SaveGame(SaveDir).Load(Load("core", "test"), _log));

        Assert.Equal("save needs ids missing from loaded mods: test:stone", error.Message);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>Значение стата, которого нет в модах, выбрасывается с записью в лог.</summary>
    [Fact]
    public void MissingStatIsDropped()
    {
        Simulation original = NewSim();
        long npc = original.CreateEntity(new WorldPos(1.5, 1.5, 0));
        new SaveGame(SaveDir).Save(original);
        _mods.Write("test/data/test.jsonc", TestData.Replace("test:mana", "test:focus"));

        Simulation loaded = new SaveGame(SaveDir).Load(Load("core", "test"), _log);

        Assert.Equal(0, loaded.Entity(npc).Stats.Value(loaded.StatTable.StatIndex("test:focus")));
        Assert.Contains($"save: dropped unknown stat test:mana of entity {npc}", _log.Infos);
    }

    /// <summary>Переписываются только изменённые регионы и регионы с сущностями.</summary>
    [Fact]
    public void OnlyChangedRegionsAreWritten()
    {
        Simulation sim = NewSim();
        SaveGame save = new(SaveDir);
        save.Save(sim);
        string first = Path.Combine(SaveDir, "regions", "r.0.0.bin");
        string second = Path.Combine(SaveDir, "regions", "r.1.0.bin");
        Assert.True(File.Exists(first) && File.Exists(second), "both regions are written on the first save");

        // Метка вместо файла первого региона покажет, переписан ли он
        File.WriteAllText(first, "marker");
        sim.World.SetFloor(new CellPos(260, 1, 0), sim.WorldTable.FloorIndex("test:stone"));
        save.Save(sim);

        Assert.Equal("marker", File.ReadAllText(first));
        Assert.NotEqual("marker", File.ReadAllText(second));
    }

    /// <summary>Сущность, ушедшая в другой регион, после загрузки одна и на новом месте.</summary>
    [Fact]
    public void EntityMovingBetweenRegionsIsNotDuplicated()
    {
        Simulation sim = NewSim();
        long npc = sim.CreateEntity(new WorldPos(250.5, 4.5, 0));
        SaveGame save = new(SaveDir);
        save.Save(sim);

        sim.Entity(npc).Position = new WorldPos(270.5, 4.5, 0);
        save.Save(sim);
        Simulation loaded = new SaveGame(SaveDir).Load(Load("core", "test"), _log);

        Assert.Single(loaded.Entities);
        Assert.Equal(270.5, loaded.Entity(npc).Position.X);
    }

    /// <summary>После записи нет временных файлов, мета читается как JSON.</summary>
    [Fact]
    public void NoTempFilesAreLeft()
    {
        Simulation sim = NewSim();
        sim.SpawnPlayer("anna");

        new SaveGame(SaveDir).Save(sim);

        Assert.Empty(Directory.GetFiles(SaveDir, "*.tmp", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(SaveDir, "players", "anna.bin")), "player file");
        Assert.Contains("\"mapId\": \"test:strip\"", File.ReadAllText(Path.Combine(SaveDir, "meta.json")));
    }

    /// <summary>Запись оборвалась после меты: старые файлы читаются по новой таблице id, счётчики не выдают занятые id.</summary>
    [Fact]
    public void SaveInterruptedAfterMetaStillLoads()
    {
        Simulation sim = NewSim();
        SaveGame save = new(SaveDir);
        save.Save(sim);
        string oldRegion = Path.Combine(Path.GetDirectoryName(SaveDir)!, "old-region.bin");
        File.Copy(Path.Combine(SaveDir, "regions", "r.0.0.bin"), oldRegion);
        long player = sim.SpawnPlayer("anna");
        sim.World.SetFloor(new CellPos(10, 1, 0), sim.WorldTable.FloorIndex("test:stone"));
        save.Save(sim);

        // Как будто после новой меты регион не успел записаться
        File.Copy(oldRegion, Path.Combine(SaveDir, "regions", "r.0.0.bin"), overwrite: true);
        SaveGame loading = new(SaveDir);
        Simulation loaded = loading.Load(Load("core", "test"), _log);
        loading.LoadPlayer(loaded, "anna", _log);
        long next = loaded.CreateEntity(new WorldPos(1.5, 1.5, 0));

        Assert.Equal("core:wood_floor", loaded.WorldTable.Floor(loaded.World.Floor(new CellPos(10, 1, 0))).Id);
        Assert.True(next > player, $"new entity {next} must not reuse player id {player}");
        Assert.True(loaded.NextItemId > sim.Entity(player).Inventory.Items[0].Id, "item counter skips player items");
    }

    /// <summary>Повреждённые и чужие файлы дают SaveException с именем файла, а не падение.</summary>
    [Theory]
    [InlineData("regions/r.0.0.bin", "garbage")]
    [InlineData("regions/r.0.0.bin", "DVRG\u0001broken")]
    [InlineData("regions/r.0.0.bin", "DVRG\u0009")]
    [InlineData("meta.json", "{ not json")]
    [InlineData("meta.json", "{}")]
    public void BrokenFilesThrowSaveException(string file, string text)
    {
        new SaveGame(SaveDir).Save(NewSim());
        File.WriteAllText(Path.Combine(SaveDir, file), text);

        SaveException error = Assert.Throws<SaveException>(() => new SaveGame(SaveDir).Load(Load("core", "test"), _log));

        Assert.StartsWith(Path.GetFileName(file), error.Message);
    }

    /// <summary>Обрезанный файл региона - SaveException.</summary>
    [Fact]
    public void TruncatedRegionThrowsSaveException()
    {
        new SaveGame(SaveDir).Save(NewSim());
        string path = Path.Combine(SaveDir, "regions", "r.0.0.bin");
        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        Assert.Throws<SaveException>(() => new SaveGame(SaveDir).Load(Load("core", "test"), _log));
    }

    /// <summary>Id игрока - имя файла, поэтому выйти им из папки сохранения нельзя.</summary>
    [Fact]
    public void PlayerIdIsChecked()
    {
        Assert.True(SaveGame.IsValidPlayerId("anna_01-x"));
        Assert.False(SaveGame.IsValidPlayerId("../anna"));
        Assert.False(SaveGame.IsValidPlayerId(string.Empty));
        Assert.False(SaveGame.IsValidPlayerId(new string('a', 65)));

        Simulation sim = NewSim();
        sim.SpawnPlayer("../escape");
        Assert.Throws<ArgumentException>(() => new SaveGame(SaveDir).Save(sim));
    }

    /// <summary>Без сохранения в папке - SaveException, игрока без файла нет.</summary>
    [Fact]
    public void MissingSaveAndPlayer()
    {
        SaveGame save = new(SaveDir);
        Assert.False(save.Exists);
        Assert.Throws<SaveException>(() => save.Load(Load("core", "test"), _log));

        save.Save(NewSim());
        Simulation loaded = save.Load(Load("core", "test"), _log);
        Assert.Null(save.LoadPlayer(loaded, "nobody", _log));
    }

    /// <summary>Тики sim.</summary>
    private static void Tick(Simulation sim, int count)
    {
        for (int i = 0; i < count; i++)
        {
            sim.Tick();
        }
    }

    /// <summary>Время, счётчики, мир и сущности двух sim совпадают.</summary>
    private static void AssertSame(Simulation expected, Simulation actual)
    {
        Assert.Equal(expected.Clock.Tick, actual.Clock.Tick);
        Assert.Equal(expected.NextEntityId, actual.NextEntityId);
        Assert.Equal(expected.NextItemId, actual.NextItemId);
        AssertSameWorld(expected, actual);
        Assert.Equal(expected.Entities.Select(entity => entity.Id).Order(), actual.Entities.Select(entity => entity.Id).Order());
        foreach (Entity entity in expected.Entities)
        {
            AssertSameEntity(expected, entity, actual, actual.Entity(entity.Id));
        }
    }

    /// <summary>Полы и рёбра совпадают по id определений.</summary>
    private static void AssertSameWorld(Simulation expected, Simulation actual)
    {
        Assert.Equal((expected.World.Width, expected.World.Height), (actual.World.Width, actual.World.Height));
        for (int z = World.MinLevel; z <= World.MaxLevel; z++)
        {
            for (int y = 0; y <= expected.World.Height; y++)
            {
                for (int x = 0; x <= expected.World.Width; x++)
                {
                    CellPos cell = new(x, y, z);
                    Assert.Equal(FloorId(expected, cell), FloorId(actual, cell));
                    Assert.Equal(EdgeId(expected, cell, true), EdgeId(actual, cell, true));
                    Assert.Equal(EdgeId(expected, cell, false), EdgeId(actual, cell, false));
                }
            }
        }
    }

    /// <summary>Позиция, статы, мудлы, инвентарь и действие совпадают.</summary>
    private static void AssertSameEntity(Simulation expectedSim, Entity expected, Simulation actualSim, Entity actual)
    {
        Assert.Equal(expected.PlayerId, actual.PlayerId);
        Assert.Equal(expected.Position, actual.Position);
        for (int i = 0; i < expectedSim.StatTable.StatCount; i++)
        {
            string stat = expectedSim.StatTable.Stat(i).Id;
            Assert.Equal(expected.Stats.Value(i), actual.Stats.Value(actualSim.StatTable.StatIndex(stat)));
            Assert.Equal(expected.Stats.Rate(i), actual.Stats.Rate(actualSim.StatTable.StatIndex(stat)), 12);
        }

        for (int i = 0; i < expectedSim.StatTable.MoodleCount; i++)
        {
            string moodle = expectedSim.StatTable.Moodle(i).Def.Id;
            Assert.Equal(expected.Stats.MoodleLevel(i), actual.Stats.MoodleLevel(actualSim.StatTable.MoodleIndex(moodle)));
        }

        Assert.Equal(expected.Inventory.Items.Select(item => (item.Id, item.Def.Id)), actual.Inventory.Items.Select(item => (item.Id, item.Def.Id)));
        Assert.Equal(Describe(expected.Action), Describe(actual.Action));
    }

    /// <summary>Действие строкой для сравнения.</summary>
    private static string Describe(ActionState? action) => action is null ? "none" : $"{action.Action.Id} {action.ItemId} {action.StartTick} {action.DurationTicks}";

    /// <summary>Id пола клетки или пустая строка.</summary>
    private static string FloorId(Simulation sim, CellPos cell)
    {
        ushort floor = sim.World.Floor(cell);
        return floor == 0 ? string.Empty : sim.WorldTable.Floor(floor).Id;
    }

    /// <summary>Id стены ребра и открыта ли дверь.</summary>
    private static string EdgeId(Simulation sim, CellPos owner, bool north)
    {
        Edge edge = sim.World.GetOwnedEdge(owner, north);
        return edge.Wall == 0 ? string.Empty : $"{sim.WorldTable.Wall(edge.Wall).Id} {edge.Open}";
    }

    /// <summary>Новый sim на полосе.</summary>
    private Simulation NewSim() => new(Load("core", "test"), _log, "test:strip");

    /// <summary>Загрузка модов по порядку.</summary>
    private ModLoadResult Load(params string[] ids)
    {
        ModLoadResult result = _mods.Load(ids);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return result;
    }

    /// <summary>Все файлы сохранения с содержимым одной строкой.</summary>
    private string Snapshot()
    {
        return string.Join("\n", Directory.GetFiles(SaveDir, "*", SearchOption.AllDirectories).Order()
            .Select(file => $"{file} {Convert.ToHexString(File.ReadAllBytes(file))}"));
    }
}
