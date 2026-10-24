using System;
using System.IO;
using DevAInt.Sim;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Saves;

/// <summary>Тесты <see cref="WorldStart"/> и <see cref="PlayerIdentity"/> во временной папке.</summary>
public class WorldStartTest : TestCase
{
    /// <summary>Временная папка теста.</summary>
    private string _root = string.Empty;

    /// <summary>Моды core.</summary>
    private ModLoadResult _mods = null!;

    /// <summary>Новая временная папка и моды перед каждым тестом.</summary>
    public override void BeforeEach()
    {
        _root = Path.Combine(Path.GetTempPath(), "devaint-godot-tests-" + Guid.NewGuid().ToString("N"));
        _mods = ModLoader.Load(ModsPath.Find(), GameMods.Ids);
    }

    /// <summary>Удаление временной папки.</summary>
    public override void AfterEach()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Без сохранения - новый мир, после записи - загрузка того же мира.</summary>
    public void TestNewThenLoaded()
    {
        ListLog log = new();
        string[] args = ["--save", "test", "--time-scale", "3"];

        (Simulation Sim, SaveGame Save)? fresh = WorldStart.Open(_mods, log, args, _root);
        fresh!.Value.Sim.Tick();
        fresh.Value.Save.Save(fresh.Value.Sim);
        (Simulation Sim, SaveGame Save)? loaded = WorldStart.Open(_mods, log, args, _root);

        CheckEqual(3.0, fresh.Value.Sim.Clock.TimeScale, "time scale");
        CheckEqual(Path.Combine(_root, "test"), fresh.Value.Save.Directory, "save folder");
        CheckEqual(1L, loaded?.Sim.Clock.Tick ?? -1, "loaded tick");
        CheckEqual("new world test on map core:starter_room", log.Infos[0], "first message");
        CheckEqual("loaded save test at tick 1", log.Infos[1], "second message");
    }

    /// <summary>Неверное имя и повреждённое сохранение - null и ошибка в логе.</summary>
    public void TestErrors()
    {
        ListLog log = new();
        Directory.CreateDirectory(Path.Combine(_root, "broken"));
        File.WriteAllText(Path.Combine(_root, "broken", "meta.json"), "{ not json");

        Check(WorldStart.Open(_mods, log, ["--save", "../escape"], _root) is null, "bad name");
        Check(WorldStart.Open(_mods, log, ["--save", "broken"], _root) is null, "broken save");
        CheckEqual("save name ../escape must be 1-64 latin letters, digits, - or _", log.Errors[0], "name error");
        Check(log.Errors.Count == 2 && log.Errors[1].StartsWith("cannot load save broken: meta.json:", StringComparison.Ordinal), $"save error: {string.Join("; ", log.Errors)}");
    }

    /// <summary>Id игрока создаётся один раз и потом читается тот же; испорченный файл заменяется.</summary>
    public void TestPlayerIdentity()
    {
        string path = Path.Combine(_root, "player_id");

        string first = PlayerIdentity.LoadOrCreate(path);
        string second = PlayerIdentity.LoadOrCreate(path);
        File.WriteAllText(path, "../bad");
        string replaced = PlayerIdentity.LoadOrCreate(path);

        CheckEqual(first, second, "same id");
        Check(SaveGame.IsValidName(first), "id is a valid name");
        Check(replaced != first && SaveGame.IsValidName(replaced), "bad file replaced");
    }
}
