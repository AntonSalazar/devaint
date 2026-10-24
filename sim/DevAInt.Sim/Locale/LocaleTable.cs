using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevAInt.Sim.Mods;

namespace DevAInt.Sim.Locale;

/// <summary>Строки по языкам из папок locale/язык/*.jsonc: ключ - текст; поздняя папка перекрывает раннюю.</summary>
public sealed partial class LocaleTable
{
    /// <summary>Строки по языкам.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _languages = [];

    /// <summary>Языки, для которых есть строки.</summary>
    public IReadOnlyCollection<string> Languages => _languages.Keys;

    /// <summary>Загрузка папок locale по порядку: сначала интерфейс, затем моды в порядке загрузки; ошибки собираются, загрузка идёт дальше.</summary>
    public static LocaleTable Load(IReadOnlyList<string> localeDirs, string shownRoot, List<ModError> errors)
    {
        LocaleTable table = new();
        foreach (string dir in localeDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            string[] languages = Directory.GetDirectories(dir);
            Array.Sort(languages, StringComparer.Ordinal);
            foreach (string languageDir in languages)
            {
                table.LoadLanguage(languageDir, shownRoot, errors);
            }
        }

        return table;
    }

    /// <summary>Строки языка: ключ - текст.</summary>
    public IReadOnlyDictionary<string, string> Strings(string language) => _languages.GetValueOrDefault(language) ?? [];

    /// <summary>Строка языка или null.</summary>
    public string? Get(string language, string key) => _languages.GetValueOrDefault(language)?.GetValueOrDefault(key);

    [GeneratedRegex("^[a-z]{2,3}(_[A-Z]{2})?$")]
    private static partial Regex LanguageRegex();

    /// <summary>Файлы одной папки языка по имени.</summary>
    private void LoadLanguage(string languageDir, string shownRoot, List<ModError> errors)
    {
        string language = Path.GetFileName(languageDir);
        if (!LanguageRegex().IsMatch(language))
        {
            errors.Add(new ModError(Path.GetRelativePath(shownRoot, languageDir), null, string.Empty, $"language folder {language} must be a code like en, ru or pt_BR"));
            return;
        }

        string[] files = Directory.GetFiles(languageDir, "*.jsonc");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            LoadFile(language, file, Path.GetRelativePath(shownRoot, file), errors);
        }
    }

    /// <summary>Один файл: плоский объект из строк; поле $schema пропускается.</summary>
    private void LoadFile(string language, string file, string shown, List<ModError> errors)
    {
        Dictionary<string, JsonElement>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file), Jsonc.Options);
        }
        catch (JsonException error)
        {
            errors.Add(Jsonc.ToError(shown, error));
            return;
        }

        if (!_languages.TryGetValue(language, out Dictionary<string, string>? strings))
        {
            _languages[language] = strings = [];
        }

        foreach ((string key, JsonElement value) in values ?? [])
        {
            if (key == "$schema")
            {
                continue;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                errors.Add(new ModError(shown, null, $"$.{key}", "text must be a string"));
                continue;
            }

            strings[key] = value.GetString()!;
        }
    }
}
