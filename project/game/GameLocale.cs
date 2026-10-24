using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevAInt.Sim.Locale;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;
using Godot;

/// <summary>Переводы игры и модов в TranslationServer Godot: строки интерфейса, затем моды в порядке загрузки.</summary>
public static class GameLocale
{
    /// <summary>Язык по умолчанию: на него же откатываются строки, которых нет в выбранном.</summary>
    public const string DefaultLanguage = "en";

    /// <summary>Добавленные переводы, чтобы при смене языка или модов убрать прежние.</summary>
    private static readonly List<Translation> _added = [];

    /// <summary>Строки из папки locale рядом с модами и из locale каждого мода; ошибки - в лог, игра идёт дальше.</summary>
    public static LocaleTable Load(ModLoadResult mods, ISimLog log)
    {
        List<string> dirs = [ModsPath.LocaleRoot(), .. mods.Mods.Select(mod => Path.Combine(mod.Directory, "locale"))];
        List<ModError> errors = [];
        LocaleTable table = LocaleTable.Load(dirs, Path.GetDirectoryName(ModsPath.Find())!, errors);
        foreach (ModError error in errors)
        {
            log.Error(error.ToString());
        }

        return table;
    }

    /// <summary>Строки всех языков в TranslationServer и выбор языка language.</summary>
    public static void Apply(LocaleTable table, string language)
    {
        foreach (Translation old in _added)
        {
            TranslationServer.RemoveTranslation(old);
        }

        _added.Clear();
        foreach (string code in table.Languages)
        {
            Translation translation = new() { Locale = code };
            foreach ((string key, string text) in table.Strings(code))
            {
                translation.AddMessage(key, text);
            }

            TranslationServer.AddTranslation(translation);
            _added.Add(translation);
        }

        // Строки, которых нет в выбранном языке, Godot берёт из запасного языка проекта, по умолчанию английского
        TranslationServer.SetLocale(language);
    }

    /// <summary>Текст по ключу на текущем языке; без перевода - fallback.</summary>
    public static string Text(string key, string fallback)
    {
        string text = TranslationServer.Translate(key);
        return text == key ? fallback : text;
    }
}
