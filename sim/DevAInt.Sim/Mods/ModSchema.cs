using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace DevAInt.Sim.Mods;

/// <summary>JSON Schema файлов мода для редактора моддера, из тех же типов и настроек, что у загрузчика.</summary>
public static class ModSchema
{
    /// <summary>Имя файла схемы data/*.jsonc.</summary>
    public const string DataFile = "data.schema.json";

    /// <summary>Имя файла схемы mod.jsonc.</summary>
    public const string ManifestFileName = "mod.schema.json";

    /// <summary>Версия стандарта JSON Schema.</summary>
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>Текст схемы data/*.jsonc.</summary>
    public static string Data() => Build(typeof(DefFile), "DevAInt mod data file");

    /// <summary>Текст схемы mod.jsonc.</summary>
    public static string Manifest() => Build(typeof(ManifestFile), "DevAInt mod manifest");

    /// <summary>Схема типа с версией стандарта и заголовком, отступы и концы строк LF.</summary>
    private static string Build(Type type, string title)
    {
        // Поля без пометки ? считаются не допускающими null, как их и проверяет загрузчик
        JsonSchemaExporterOptions exporter = new() { TreatNullObliviousAsNonNullable = true };
        JsonObject root = Jsonc.Options.GetJsonSchemaAsNode(type, exporter).AsObject();
        root.Insert(0, "$schema", Dialect);
        root.Insert(1, "title", title);

        string text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return text.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}
