using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevAInt.Sim.Data;

/// <summary>
/// Класс правил игры.
/// </summary>
public sealed class Rules
{
    /// <summary>
    /// Название файла правил игры.
    /// </summary>
    private const string RulesFile = "rules.json";

    /// <summary>
    /// Название файла с описанием узлов.
    /// </summary>
    private const string NodesFile = "nodes.json";

    /// <summary>
    /// Название файла с описанием эксплойтов.
    /// </summary>
    private const string ExploitsFile = "exploits.json";

    /// <summary>
    /// Название файла с описанием модификаторов/мутаторов.
    /// </summary>
    private const string ModifiersFile = "modifiers.json";

    /// <summary>
    /// Название файла с описанием доступных событий в игре.
    /// </summary>
    private const string EventsFile = "events.json";

    /// <summary>
    /// Название файла с описанием генерации карты.
    /// </summary>
    private const string MapGenFile = "mapgen.json";

    /// <summary>
    /// Название файла с описанем поведения AI-ботов.
    /// </summary>
    private const string AiFile = "ai.json";

    /// <summary>
    /// Название файла с доступными фракциями в игре.
    /// </summary>
    private const string FactionsFile = "factions.json";

    /// <summary>
    /// Статичный метод опций при считывании JSON файлов.
    /// </summary>
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Приватный конструктор.
    /// </summary>
    /// <param name="core">Правила игры.</param>
    /// <param name="nodeTypes">Таблица узлов.</param>
    /// <param name="exploits">Таблица эксплойтов.</param>
    /// <param name="modifiers">Таблица модификаторов.</param>
    /// <param name="events">Список событий.</param>
    /// <param name="mapGen">Генерация карты.</param>
    /// <param name="profiles">Профили кластеров.</param>
    /// <param name="aiProfiles">Профили AI-ботов.</param>
    /// <param name="presets">Пресеты фракций.</param>
    private Rules(
        RulesDef core,
        Dictionary<NodeType, NodeTypeDef> nodeTypes,
        Dictionary<string, ExploitDef> exploits,
        Dictionary<string, Modifier> modifiers,
        IReadOnlyList<WorldEventDef> events,
        MapGenDef mapGen,
        Dictionary<string, ClusterProfile> profiles,
        Dictionary<string, AiProfileDef> aiProfiles,
        Dictionary<string, FactionPresetDef> presets)
    {
        Core = core;
        NodeTypes = nodeTypes;
        Exploits = exploits;
        Modifiers = modifiers;
        Events = events;
        MapGen = mapGen;
        Profiles = profiles;
        AiProfiles = aiProfiles;
        Presets = presets;
    }

    /// <summary>
    /// Описание правил игры.
    /// </summary>
    public RulesDef Core { get; }

    /// <summary>
    /// Таблица доступных типов узлов.
    /// </summary>
    public IReadOnlyDictionary<NodeType, NodeTypeDef> NodeTypes { get; }

    /// <summary>
    /// Таблица доступных эксплойтов.
    /// </summary>
    public IReadOnlyDictionary<string, ExploitDef> Exploits { get; }

    /// <summary>
    /// Таблица модификаторов.
    /// </summary>
    public IReadOnlyDictionary<string, Modifier> Modifiers { get; }

    /// <summary>
    /// Список доступных игровых событий.
    /// </summary>
    public IReadOnlyList<WorldEventDef> Events { get; }

    /// <summary>
    /// Описание для генерации карты.
    /// </summary>
    public MapGenDef MapGen { get; }

    /// <summary>
    /// Таблица профилей кластеров.
    /// </summary>
    public IReadOnlyDictionary<string, ClusterProfile> Profiles { get; }

    /// <summary>
    /// Таблица профилей для AI-ботов.
    /// </summary>
    public IReadOnlyDictionary<string, AiProfileDef> AiProfiles { get; }

    /// <summary>
    /// Таблица доступных фракций в игре.
    /// </summary>
    public IReadOnlyDictionary<string, FactionPresetDef> Presets { get; }


    /// <summary>
    /// Статичный метод загрузки данных для формирования правил.
    /// </summary>
    /// <param name="dir">Путь до директории с правилами.</param>
    /// <returns>Вернет сформированный экземпляр правил.</returns>
    public static Rules Load(string dir)
    {
        // 1. Читаем файлы.
        RulesDef core = ReadFile<RulesDef>(dir, RulesFile);
        NodeTypeDef[] nodes = ReadFile<NodeTypeDef[]>(dir, NodesFile);
        ExploitDef[] exploits = ReadFile<ExploitDef[]>(dir, ExploitsFile);
        Modifier[] modifiers = ReadFile<Modifier[]>(dir, ModifiersFile);
        WorldEventDef[] events = ReadFile<WorldEventDef[]>(dir, EventsFile);
        MapGenDef mapGen = ReadFile<MapGenDef>(dir, MapGenFile);
        AiProfileDef[] aiProfiles = ReadFile<AiProfileDef[]>(dir, AiFile);
        FactionPresetDef[] presets = ReadFile<FactionPresetDef[]>(dir, FactionsFile);

        // 2. Раскладываем по ключам (дубликат — ошибка с именем файла).
        Rules rules = new(
            core,
            ToIndex(nodes, NodesFile, static n => n.Type),
            ToIndex(exploits, ExploitsFile, static e => e.Id),
            ToIndex(modifiers, ModifiersFile, static m => m.Id),
            events,
            mapGen,
            ToIndex(mapGen.Profiles, MapGenFile, static p => p.Id),
            ToIndex(aiProfiles, AiFile, static a => a.Id),
            ToIndex(presets, FactionsFile, static p => p.Id));

        // 3. Проверяем целостность.
        rules.Validate();
        return rules;
    }


    /// <summary>
    /// Метод возврата условий победы по количеству фракций.
    /// </summary>
    /// <param name="factions">Количество фракций в игре.</param>
    /// <returns>Условия победы.</returns>
    public VictoryDef VictoryFor(int factions) =>
        Core.Victory.FirstOrDefault(v => v.Factions == factions)
        ?? throw new ArgumentOutOfRangeException(nameof(factions));


    /// <summary>
    /// Метод возврата размера карты по количеству фракций.
    /// </summary>
    /// <param name="factions">Количество фракций в игре.</param>
    /// <returns>Размер карты.</returns>
    public MapSizeDef MapSizeFor(int factions) =>
        MapGen.Sizes.FirstOrDefault(s => s.Factions == factions)
        ?? throw new ArgumentOutOfRangeException(nameof(factions));


    /// <summary>
    /// Статичный метод считывания файла.
    /// </summary>
    /// <param name="dir">Директория, где лежит файл.</param>
    /// <param name="file">Название файла.</param>
    /// <typeparam name="T">Тип параметров, которые будут взяты.</typeparam>
    /// <returns>Вернет параметры из файла.</returns>
    private static T ReadFile<T>(string dir, string file)
    {
        // Сформируем путь.
        string path = Path.Combine(dir, file);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{file}: data file not found", path);
        }

        // Читаем текст и десериализуем (вытаскиваем данные).
        string text = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(text, _options)
            ?? throw new InvalidDataException($"{file}: empty document");
    }


    /// <summary>
    /// Метод сборки словаря по ключу.
    /// </summary>
    /// <param name="items">Список значений.</param>
    /// <param name="file">Файл.</param>
    /// <param name="key">Функция, которая из элемента достает ключ.</param>
    /// <typeparam name="TKey">Тип ключа.</typeparam>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <returns>Вернёт таблицу.</returns>
    private static Dictionary<TKey, T> ToIndex<TKey, T>(T[] items, string file, Func<T, TKey> key)
        where TKey : notnull
    {
        Dictionary<TKey, T> index = [];
        foreach (T item in items)
        {
            TKey id = key(item);
            if (!index.TryAdd(id, item))
            {
                throw new InvalidDataException($"{file}: duplicate id `{id}`");
            }
        }
        return index;
    }


    /// <summary>
    /// Метод для определения зависимостей.
    /// </summary>
    /// <param name="index">Проверяемый индекс.</param>
    /// <param name="id">Ключ.</param>
    /// <param name="file">В каком файле.</param>
    /// <param name="owner">Владелец.</param>
    /// <typeparam name="T">Целевой тип.</typeparam>
    private static void Require<T>(IReadOnlyDictionary<string, T> index, string id, string file, string owner)
    {
        if (!index.ContainsKey(id))
        {
            throw new InvalidDataException($"{file}: '{owner}' references unknown id '{id}'");
        }
    }


    /// <summary>
    /// Метод проверки циклической зависимости.
    /// </summary>
    /// <param name="modifiers">Таблица модификаторов.</param>
    private static void CheckNoCycles(IReadOnlyDictionary<string, Modifier> modifiers)
    {
        HashSet<string> done = [];
        HashSet<string> path = [];

        foreach (string id in modifiers.Keys)
        {
            Visit(id);
        }

        void Visit(string id)
        {
            if (done.Contains(id))
            {
                return;
            }
            if (!path.Add(id))
            {
                throw new InvalidDataException($"{ModifiersFile}: requires cycle at '{id}'");
            }

            foreach (string req in modifiers[id].Requires)
            {
                Visit(req);
            }

            path.Remove(id);
            done.Add(id);
        }
    }


    /// <summary>
    /// Метод валидирования правил.
    /// </summary>
    private void Validate()
    {
        // Ссылки.
        foreach (string id in Core.StartingExploits)
        {
            Require(Exploits, id, RulesFile, "startingExploits");
        }
        foreach (Modifier mod in Modifiers.Values)
        {
            foreach (string req in mod.Requires)
            {
                Require(Modifiers, req, ModifiersFile, mod.Id);
            }
            if (mod.StartingExploit is not null)
            {
                Require(Exploits, mod.StartingExploit, ModifiersFile, mod.Id);
            }
        }

        foreach (FactionPresetDef preset in Presets.Values)
        {
            foreach (string id in preset.Mutators)
            {
                Require(Modifiers, id, FactionsFile, preset.Id);
            }
            Require(AiProfiles, preset.AiProfile, FactionsFile, preset.Id);
        }

        // Все типы узлов, кроме Empty.
        foreach (NodeType type in Enum.GetValues<NodeType>())
        {
            if (type != NodeType.Empty && !NodeTypes.ContainsKey(type))
            {
                throw new InvalidDataException($"{NodesFile}: node type '{type}' is not defined");
            }
        }
        // Таблицы по числу фракций — на каждый N.
        for (int n = 2; n <= Core.MaxFactions; n++)
        {
            if (!Core.Victory.Any(v => v.Factions == n))
            {
                throw new InvalidDataException($"{RulesFile}: no victory row for {n} factions");
            }
            if (!MapGen.Sizes.Any(s => s.Factions == n))
            {
                throw new InvalidDataException($"{MapGenFile}: no map size row for {n} factions");
            }
        }

        CheckNoCycles(Modifiers);
    }
}
