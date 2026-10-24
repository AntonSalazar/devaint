using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevAInt.Sim.Mods;
using Json.Schema;
using Xunit;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Тесты <see cref="ModSchema"/>: схема в репозитории свежая и описывает файлы модов так же, как загрузчик.</summary>
public class ModSchemaTest
{
    /// <summary>Настройки разбора JSONC для проверки по схеме.</summary>
    private static readonly JsonDocumentOptions _jsonc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Папка schemas репозитория рядом с mods.</summary>
    private static string SchemasDir => Path.Combine(Path.GetDirectoryName(RepoMods.Root)!, "schemas");

    /// <summary>Схемы в репозитории совпадают с генерацией из кода.</summary>
    [Fact]
    public void RepoSchemasAreUpToDate()
    {
        string data = File.ReadAllText(Path.Combine(SchemasDir, ModSchema.DataFile));
        string manifest = File.ReadAllText(Path.Combine(SchemasDir, ModSchema.ManifestFileName));

        Assert.True(data == ModSchema.Data(), "schemas/data.schema.json is outdated, run just schema");
        Assert.True(manifest == ModSchema.Manifest(), "schemas/mod.schema.json is outdated, run just schema");
        Assert.True(File.ReadAllText(Path.Combine(SchemasDir, ModSchema.LocaleFile)) == ModSchema.Locale(), "schemas/locale.schema.json is outdated, run just schema");
    }

    /// <summary>Все файлы мода core проходят свои схемы.</summary>
    [Fact]
    public void CoreFilesMatchSchemas()
    {
        string core = Path.Combine(RepoMods.Root, "core");
        List<string> problems = [.. Problems(ModSchema.Manifest(), File.ReadAllText(Path.Combine(core, "mod.jsonc")))];
        foreach (string file in Directory.GetFiles(Path.Combine(core, "data"), "*.jsonc"))
        {
            problems.AddRange(Problems(ModSchema.Data(), File.ReadAllText(file)).Select(problem => $"{Path.GetFileName(file)}: {problem}"));
        }

        foreach (string file in Directory.GetFiles(Path.Combine(core, "locale"), "*.jsonc", SearchOption.AllDirectories))
        {
            problems.AddRange(Problems(ModSchema.Locale(), File.ReadAllText(file)).Select(problem => $"{Path.GetFileName(file)}: {problem}"));
        }

        Assert.Empty(problems);
        Assert.NotEmpty(Problems(ModSchema.Locale(), """{ "k:a": 5 }"""));
    }

    /// <summary>Каждый файл core ссылается на свою схему относительным путём.</summary>
    [Fact]
    public void CoreFilesReferenceSchemas()
    {
        string core = Path.Combine(RepoMods.Root, "core");
        foreach (string file in Directory.GetFiles(Path.Combine(core, "data"), "*.jsonc"))
        {
            Assert.Equal("../../../schemas/data.schema.json", SchemaRef(file));
        }

        Assert.Equal("../../schemas/mod.schema.json", SchemaRef(Path.Combine(core, "mod.jsonc")));
    }

    /// <summary>Схема ловит опечатку в имени поля, пропущенное обязательное поле и неверный вариант перечисления.</summary>
    [Theory]
    [InlineData("""{ "stats": [ { "id": "a:b", "min": 0, "max": 1, "rait": 1 } ] }""")]
    [InlineData("""{ "stats": [ { "id": "a:b", "min": 0 } ] }""")]
    [InlineData("""{ "patches": [ { "op": "replace", "target": "a:b" } ] }""")]
    [InlineData("""{ "statz": [] }""")]
    public void SchemaRejectsBadData(string text) => Assert.NotEmpty(Problems(ModSchema.Data(), text));

    /// <summary>Манифест без версии API схема не пропускает.</summary>
    [Fact]
    public void SchemaRejectsManifestWithoutApi() => Assert.NotEmpty(Problems(ModSchema.Manifest(), """{ "id": "a", "name": "A", "version": "0.1.0" }"""));

    /// <summary>Загрузчик пропускает поле $schema в данных и манифесте.</summary>
    [Fact]
    public void LoaderSkipsSchemaField()
    {
        using TempMods mods = new();
        mods.Write("test/mod.jsonc", """{ "$schema": "../../schemas/mod.schema.json", "id": "test", "name": "Test", "version": "0.1.0", "api": 1 }""");
        mods.Write("test/data/stats.jsonc", """{ "$schema": "x", "stats": [ { "id": "test:hp", "min": 0, "max": 10 } ] }""");

        ModLoadResult result = mods.Load("test");

        Assert.True(result.Ok, string.Join("\n", result.Errors));
    }

    /// <summary>Ошибки проверки текста по схеме, пусто если текст подходит.</summary>
    private static IEnumerable<string> Problems(string schemaText, string jsonc)
    {
        JsonSchema schema = JsonSchema.FromText(schemaText);
        EvaluationResults results = schema.Evaluate(JsonNode.Parse(jsonc, null, _jsonc), new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
        {
            return [];
        }

        return results.Details.Where(detail => detail.Errors is not null)
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}"));
    }

    /// <summary>Значение $schema в файле.</summary>
    private static string? SchemaRef(string file) => JsonNode.Parse(File.ReadAllText(file), null, _jsonc)?["$schema"]?.GetValue<string>();
}
