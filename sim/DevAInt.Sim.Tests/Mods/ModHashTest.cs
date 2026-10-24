using System.IO;
using DevAInt.Sim.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Тесты <see cref="ModHash"/> и сверки модов <see cref="ModInfo"/>.</summary>
public class ModHashTest
{
    /// <summary>Манифест для тестов.</summary>
    private const string Manifest = """{ "id": "test", "name": "Test", "version": "0.1.0", "api": 1 }""";

    /// <summary>Те же данные с концами строк CRLF и LF дают один хеш.</summary>
    [Fact]
    public void LineEndingsDoNotChangeHash()
    {
        using TempMods unix = new();
        using TempMods windows = new();
        unix.Write("test/mod.jsonc", Manifest).Write("test/data/a.jsonc", "{\n  \"stats\": []\n}\n");
        windows.Write("test/mod.jsonc", Manifest).Write("test/data/a.jsonc", "{\r\n  \"stats\": []\r\n}\r\n");

        Assert.Equal(Hash(unix), Hash(windows));
    }

    /// <summary>Правка данных или манифеста меняет хеш, правка скрипта или перевода - нет.</summary>
    [Fact]
    public void DataChangesHashScriptsDoNot()
    {
        using TempMods mods = new();
        mods.Write("test/mod.jsonc", Manifest).Write("test/data/a.jsonc", "{}").Write("test/scripts/a.lua", "x = 1");
        string before = Hash(mods);

        mods.Write("test/scripts/a.lua", "x = 2").Write("test/locale/ru/a.jsonc", """{ "k": "v" }""");
        string afterScript = Hash(mods);
        mods.Write("test/data/a.jsonc", "{ }");
        string afterData = Hash(mods);

        Assert.Equal(before, afterScript);
        Assert.NotEqual(before, afterData);
        Assert.Equal(64, before.Length);
    }

    /// <summary>Текст, перенесённый из одного файла в другой, меняет хеш.</summary>
    [Fact]
    public void MovingTextBetweenFilesChangesHash()
    {
        using TempMods first = new();
        using TempMods second = new();
        first.Write("test/mod.jsonc", Manifest).Write("test/data/a.jsonc", "{}{}").Write("test/data/b.jsonc", string.Empty);
        second.Write("test/mod.jsonc", Manifest).Write("test/data/a.jsonc", "{}").Write("test/data/b.jsonc", "{}");

        Assert.NotEqual(Hash(first), Hash(second));
    }

    /// <summary>Загрузчик кладёт хеш в манифест, сведения для сети берут id, версию и хеш.</summary>
    [Fact]
    public void LoaderStoresHash()
    {
        using TempMods mods = new();
        mods.Write("test/mod.jsonc", Manifest);

        ModManifest manifest = Assert.Single(mods.Load("test").Mods);

        Assert.Equal(new ModInfo("test", "0.1.0", Hash(mods)), manifest.Info);
    }

    /// <summary>Одинаковые моды в одном порядке совпадают.</summary>
    [Fact]
    public void SameModsHaveNoDifference() => Assert.Null(ModInfo.Difference([Core("h1")], [Core("h1")]));

    /// <summary>Каждое расхождение описано отдельно и понятно.</summary>
    [Fact]
    public void DifferencesAreDescribed()
    {
        ModInfo zombies = new("zombies", "0.2.0", "h2");

        Assert.Equal("mods differ from the server: mod core 0.1.0 has different data", ModInfo.Difference([Core("h1")], [Core("h9")]));
        Assert.Equal("mods differ from the server: mod core is 0.1.0, server has 0.2.0", ModInfo.Difference([Core("h1")], [new("core", "0.2.0", "h1")]));
        Assert.Equal("mods differ from the server: missing mod zombies 0.2.0", ModInfo.Difference([Core("h1")], [Core("h1"), zombies]));
        Assert.Equal("mods differ from the server: extra mod zombies", ModInfo.Difference([Core("h1"), zombies], [Core("h1")]));
        Assert.Equal("mods differ from the server: mod load order differs", ModInfo.Difference([zombies, Core("h1")], [Core("h1"), zombies]));
    }

    /// <summary>Хеш мода test из временной папки.</summary>
    private static string Hash(TempMods mods) => ModHash.Compute(Path.Combine(mods.Root, "test"));

    /// <summary>Мод core версии 0.1.0 с хешом.</summary>
    private static ModInfo Core(string hash) => new("core", "0.1.0", hash);
}
