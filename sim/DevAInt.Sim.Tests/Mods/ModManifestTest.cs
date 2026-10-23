using System.Linq;
using DevAInt.Sim.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Тесты чтения манифестов и порядка загрузки в <see cref="ModLoader"/>.</summary>
public class ModManifestTest
{
    /// <summary>Зависимость грузится раньше зависящего, независимые - по id.</summary>
    [Fact]
    public void OrdersByDependsThenById()
    {
        using TempMods mods = new();
        mods.Manifest("zeta").Manifest("alpha", depends: "zeta").Manifest("beta");

        ModLoadResult result = mods.Load("alpha", "beta", "zeta");

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(["beta", "zeta", "alpha"], result.Mods.Select(mod => mod.Id));
    }

    /// <summary>loadAfter упорядочивает, если мод есть, и не мешает, если его нет.</summary>
    [Fact]
    public void LoadAfterOnlyOrders()
    {
        using TempMods mods = new();
        mods.Manifest("beta");
        mods.Write("alpha/mod.jsonc", """{ "id": "alpha", "name": "a", "version": "0.1", "api": 1, "loadAfter": ["beta", "missing"] }""");

        ModLoadResult result = mods.Load("alpha", "beta");

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        Assert.Equal(["beta", "alpha"], result.Mods.Select(mod => mod.Id));
    }

    /// <summary>Отсутствующая зависимость - ошибка с путём в манифесте.</summary>
    [Fact]
    public void MissingDependencyIsError()
    {
        using TempMods mods = new();
        mods.Manifest("alpha", depends: "core");

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal("mods/alpha/mod.jsonc: $.depends[0]: mod core not found", error.ToString());
    }

    /// <summary>Слишком старая зависимость - ошибка.</summary>
    [Fact]
    public void OldDependencyIsError()
    {
        using TempMods mods = new();
        mods.Manifest("core", version: "0.1").Manifest("alpha", depends: "core >= 0.2");

        ModError error = Assert.Single(mods.Load("alpha", "core").Errors);

        Assert.Contains("requires core 0.2.0 or newer, found 0.1.0", error.Message);
    }

    /// <summary>Цикл зависимостей - ошибка со списком модов цикла.</summary>
    [Fact]
    public void CycleIsError()
    {
        using TempMods mods = new();
        mods.Manifest("alpha", depends: "beta").Manifest("beta", depends: "alpha");

        ModError error = Assert.Single(mods.Load("alpha", "beta").Errors);

        Assert.Equal("circular mod dependency: alpha, beta", error.ToString());
    }

    /// <summary>Нет файла манифеста.</summary>
    [Fact]
    public void MissingManifestIsError()
    {
        using TempMods mods = new();

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal("mods/alpha/mod.jsonc: manifest file not found", error.ToString());
    }

    /// <summary>Id в манифесте, API и версия проверяются, ошибки собираются все.</summary>
    [Fact]
    public void ManifestFieldsAreChecked()
    {
        using TempMods mods = new();
        mods.Write("alpha/mod.jsonc", """{ "id": "other", "name": "a", "version": "1", "api": 99, "depends": ["Bad"] }""");

        string[] paths = [.. mods.Load("alpha").Errors.Select(error => error.JsonPath)];

        Assert.Equal(["$.id", "$.api", "$.version", "$.depends[0]"], paths);
    }

    /// <summary>Синтаксическая ошибка в манифесте указывает строку.</summary>
    [Fact]
    public void SyntaxErrorHasLine()
    {
        using TempMods mods = new();
        mods.Write("alpha/mod.jsonc", "{\n  \"id\": \"alpha\",\n  \"name\" \"a\"\n}");

        ModError error = Assert.Single(mods.Load("alpha").Errors);

        Assert.Equal("mods/alpha/mod.jsonc", error.File);
        Assert.Equal(3, error.Line);
    }
}
