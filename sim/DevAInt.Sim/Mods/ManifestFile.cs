using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevAInt.Sim.Mods;

/// <summary>Содержимое mod.jsonc до проверки.</summary>
internal sealed record ManifestFile
{
    /// <summary>Путь к JSON Schema для редактора, загрузчик его пропускает.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    /// <summary>Id мода, совпадает с именем папки.</summary>
    public required string Id { get; init; }

    /// <summary>Название для людей.</summary>
    public required string Name { get; init; }

    /// <summary>Версия мода.</summary>
    public required string Version { get; init; }

    /// <summary>Версия API ядра, для которой написан мод.</summary>
    public required int Api { get; init; }

    /// <summary>Зависимости вида id или id >= 0.1.</summary>
    public List<string> Depends { get; init; } = [];

    /// <summary>Моды, после которых грузиться, если они есть.</summary>
    public List<string> LoadAfter { get; init; } = [];
}
