using System;
using System.IO;
using System.Linq;
using DevAInt.Sim.Locale;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;

/// <summary>Тесты <see cref="GameLocale"/>: переводы в TranslationServer, откат на английский, перевод HUD.</summary>
public class GameLocaleTest : TestCase
{
    /// <summary>Возврат к английскому без переводов, чтобы другие тесты видели имена из id.</summary>
    public override void AfterEach() => GameLocale.Apply(new LocaleTable(), GameLocale.DefaultLanguage);

    /// <summary>Строки модов и интерфейса из репозитория на русском и английском.</summary>
    public void TestRepoTranslations()
    {
        ListLog log = new();
        LocaleTable table = GameLocale.Load(ModLoader.Load(ModsPath.Find(), GameMods.Ids), log);

        // Русские строки берутся из самих файлов перевода: тестовые данные пишутся латиницей
        GameLocale.Apply(table, "ru");
        string russian = GameLocale.Text("core:hunger", "?");
        string zombie = GameLocale.Text("zombies:zombie", "?");
        GameLocale.Apply(table, "en");
        string english = GameLocale.Text("core:hunger", "?");

        CheckEqual(table.Get("ru", "core:hunger"), russian, "russian");
        Check(russian != english, "russian differs from english");
        CheckEqual(table.Get("ru", "zombies:zombie"), zombie, "zombies mod");
        CheckEqual("Hunger", english, "english");
        CheckEqual(0, log.Errors.Count, "no errors");
    }

    /// <summary>Строки, которой нет в выбранном языке, берётся английская; нет нигде - запасной текст.</summary>
    public void TestFallback()
    {
        string root = Path.Combine(Path.GetTempPath(), "devaint-locale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "en"));
        Directory.CreateDirectory(Path.Combine(root, "ru"));
        File.WriteAllText(Path.Combine(root, "en", "a.jsonc"), """{ "k:only_en": "English only", "k:both": "Both" }""");
        File.WriteAllText(Path.Combine(root, "ru", "a.jsonc"), """{ "k:both": "Both RU" }""");
        LocaleTable table = LocaleTable.Load([root], root, []);

        GameLocale.Apply(table, "ru");

        CheckEqual("Both RU", GameLocale.Text("k:both", "?"), "russian");
        CheckEqual("English only", GameLocale.Text("k:only_en", "?"), "english fallback");
        CheckEqual("fallback", GameLocale.Text("k:missing", "fallback"), "missing key");
        Directory.Delete(root, recursive: true);
    }

    /// <summary>HUD показывает названия и надпись о смерти на выбранном языке.</summary>
    public void TestHudIsTranslated()
    {
        GameFixture game = new();
        LocaleTable table = GameLocale.Load(game.Loaded, new ListLog());
        GameLocale.Apply(table, "ru");
        HudModel model = new(ClientTables.From(game.Sim), game.Client.Replica);
        Hud hud = new();
        hud.Init(model);
        game.Tick();
        hud.Refresh();

        Check(model.Bar("core:health").Name == table.Get("ru", "core:health"), "health bar in russian");
        CheckEqual($"{table.Get("ru", "core:apple")} x3", model.Items().Single(), "items in russian");
        CheckEqual(table.Get("ru", "ui.you_died"), hud.DeathText, "death text");
        Check(hud.DeathText != "You died", "death text is translated");
        hud.Free();
    }

    /// <summary>Язык из --lang, по умолчанию английский.</summary>
    public void TestLanguageArg()
    {
        CheckEqual("ru", GameArgs.Language(["--lang", "ru"]), "ru");
        CheckEqual("en", GameArgs.Language([]), "default");
    }
}
