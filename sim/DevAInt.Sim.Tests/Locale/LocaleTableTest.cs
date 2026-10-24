using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Locale;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Locale;

/// <summary>Тесты <see cref="LocaleTable"/> и переводов в репозитории.</summary>
public class LocaleTableTest
{
    /// <summary>Корень репозитория.</summary>
    private static string RepoRoot => Path.GetDirectoryName(RepoMods.Root)!;

    /// <summary>Папки locale репозитория: интерфейс и моды.</summary>
    private static string[] RepoLocales => [Path.Combine(RepoRoot, "locale"), Path.Combine(RepoMods.Root, "core", "locale"), Path.Combine(RepoMods.Root, "zombies", "locale")];

    /// <summary>Поздняя папка перекрывает раннюю, языки независимы, $schema пропускается.</summary>
    [Fact]
    public void LaterFolderOverrides()
    {
        using TempMods temp = new();
        temp.Write("a/locale/en/one.jsonc", """{ "$schema": "x", "k:a": "A1", "k:b": "B" }""");
        temp.Write("b/locale/en/two.jsonc", """{ "k:a": "A2" }""");
        temp.Write("b/locale/ru/two.jsonc", """{ "k:a": "A RU" }""");
        List<ModError> errors = [];

        LocaleTable table = LocaleTable.Load([Path.Combine(temp.Root, "a", "locale"), Path.Combine(temp.Root, "b", "locale"), Path.Combine(temp.Root, "none")], temp.Root, errors);

        Assert.Empty(errors);
        Assert.Equal("A2", table.Get("en", "k:a"));
        Assert.Equal("B", table.Get("en", "k:b"));
        Assert.Equal("A RU", table.Get("ru", "k:a"));
        Assert.Null(table.Get("ru", "k:b"));
        Assert.Null(table.Get("en", "$schema"));
    }

    /// <summary>Сломанный файл, не строка и неверная папка языка - ошибки, остальное грузится.</summary>
    [Fact]
    public void ErrorsDoNotStopLoading()
    {
        using TempMods temp = new();
        temp.Write("a/locale/en/bad.jsonc", "{ broken");
        temp.Write("a/locale/en/good.jsonc", """{ "k:a": "A", "k:n": 5 }""");
        temp.Write("a/locale/English/x.jsonc", """{ "k:a": "A" }""");
        List<ModError> errors = [];

        LocaleTable table = LocaleTable.Load([Path.Combine(temp.Root, "a", "locale")], temp.Root, errors);

        Assert.Equal("A", table.Get("en", "k:a"));
        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, error => error.Message == "language folder English must be a code like en, ru or pt_BR");
        Assert.Contains(errors, error => error is { JsonPath: "$.k:n", Message: "text must be a string" });
        Assert.Contains(errors, error => error.File == Path.Combine("a", "locale", "en", "bad.jsonc"));
    }

    /// <summary>Переводы репозитория грузятся без ошибок, у русского и английского одни и те же ключи.</summary>
    [Fact]
    public void RepoLanguagesHaveSameKeys()
    {
        List<ModError> errors = [];
        LocaleTable table = LocaleTable.Load(RepoLocales, RepoRoot, errors);

        Assert.Empty(errors);
        Assert.Equal(["en", "ru"], table.Languages.Order());
        Assert.Equal(table.Strings("en").Keys.Order(), table.Strings("ru").Keys.Order());
    }

    /// <summary>У каждого определения core и zombies есть английское название.</summary>
    [Fact]
    public void EveryDefinitionHasEnglishName()
    {
        ModLoadResult mods = ModLoader.Load(RepoMods.Root, ["core", "zombies"]);
        LocaleTable table = LocaleTable.Load(RepoLocales, RepoRoot, []);
        IEnumerable<string> ids = mods.Defs.All<IDef>().Select(def => def.Id)
            .Concat(mods.Defs.All<StatDef>().SelectMany(stat => stat.Moodles).Select(moodle => moodle.Id));

        Assert.DoesNotContain(ids, id => table.Get("en", id) is null);
    }

    /// <summary>Файлы перевода репозитория ссылаются на схему перевода.</summary>
    [Fact]
    public void RepoLocaleFilesReferenceSchema()
    {
        foreach (string file in RepoLocales.SelectMany(dir => Directory.GetFiles(dir, "*.jsonc", SearchOption.AllDirectories)))
        {
            string expected = string.Concat(Enumerable.Repeat("../", Path.GetRelativePath(RepoRoot, file).Count(c => c == Path.DirectorySeparatorChar))) + "schemas/locale.schema.json";
            Assert.Contains($"\"$schema\": \"{expected}\"", File.ReadAllText(file));
        }
    }
}
