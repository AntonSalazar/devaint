using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevAInt.Sim.Mods;

/// <summary>Общие настройки чтения JSONC и перевод ошибок System.Text.Json в ModError.</summary>
internal static class Jsonc
{
    /// <summary>Настройки сериализатора: комментарии, висячие запятые, camelCase, неизвестные поля - ошибка.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Настройки разбора дерева JSON с теми же послаблениями.</summary>
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Ошибка загрузки из исключения сериализатора, строка с единицы.</summary>
    public static ModError ToError(string file, JsonException error)
    {
        int? line = error.LineNumber is long number ? (int)number + 1 : null;
        return new ModError(file, line, error.Path ?? string.Empty, ShortMessage(error.Message));
    }

    /// <summary>Текст исключения без хвоста с путём и позицией, они уже есть в ModError.</summary>
    public static string ShortMessage(string message)
    {
        int index = message.IndexOf(" Path:", StringComparison.Ordinal);
        return index >= 0 ? message[..index] : message;
    }
}
