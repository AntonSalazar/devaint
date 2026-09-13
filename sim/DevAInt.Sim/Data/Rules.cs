using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevAInt.Sim.Data;

/// <summary>
/// Класс правил игры: все таблицы из <c>data/*.json</c>, загруженные и проверенные один раз.
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
    /// Название файла с описанием мутаторов.
    /// </summary>
    private const string ModifiersFile = "modifiers.json";

    /// <summary>
    /// Название файла с описанием мировых событий.
    /// </summary>
    private const string EventsFile = "events.json";

    /// <summary>
    /// Название файла с описанием генерации карты.
    /// </summary>
    private const string MapGenFile = "mapgen.json";

    /// <summary>
    /// Опции чтения JSON: имена без учёта регистра, enum-ы строками.
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
    /// <param name="modifiers">Таблица мутаторов.</param>
    /// <param name="events">Список событий.</param>
    /// <param name="mapGen">Генерация карты.</param>
    /// <param name="profiles">Профили кластеров.</param>
    private Rules(
        RulesDef core,
        Dictionary<NodeType, NodeTypeDef> nodeTypes,
        Dictionary<string, ExploitDef> exploits,
        Dictionary<string, Modifier> modifiers,
        IReadOnlyList<WorldEventDef> events,
        MapGenDef mapGen,
        Dictionary<string, ClusterProfile> profiles)
    {
        Core = core;
        NodeTypes = nodeTypes;
        Exploits = exploits;
        Modifiers = modifiers;
        Events = events;
        MapGen = mapGen;
        Profiles = profiles;
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
    /// Таблица мутаторов.
    /// </summary>
    public IReadOnlyDictionary<string, Modifier> Modifiers { get; }

    /// <summary>
    /// Список мировых событий.
    /// </summary>
    public IReadOnlyList<WorldEventDef> Events { get; }

    /// <summary>
    /// Описание для генерации карты.
    /// </summary>
    public MapGenDef MapGen { get; }

    /// <summary>
    /// Таблица профилей кластеров по Id.
    /// </summary>
    public IReadOnlyDictionary<string, ClusterProfile> Profiles { get; }


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

        // 2. Раскладываем по ключам (дубликат — ошибка с именем файла).
        Rules rules = new(
            core,
            ToIndex(nodes, NodesFile, static n => n.Type),
            ToIndex(exploits, ExploitsFile, static e => e.Id),
            ToIndex(modifiers, ModifiersFile, static m => m.Id),
            events,
            mapGen,
            ToIndex(mapGen.Profiles, MapGenFile, static p => p.Id));

        // 3. Проверяем целостность.
        rules.Validate();
        return rules;
    }


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
    /// Метод проверки, что ссылка по Id ведёт на существующую запись.
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
    /// Метод валидирования правил (<c>02-DATA-MODEL.md §8</c>).
    /// </summary>
    private void Validate()
    {
        // Ссылки на эксплойты.
        foreach (string id in Core.StartingExploits)
        {
            Require(Exploits, id, RulesFile, "startingExploits");
        }
        foreach (Modifier mod in Modifiers.Values)
        {
            if (mod.StartingExploit is not null)
            {
                Require(Exploits, mod.StartingExploit, ModifiersFile, mod.Id);
            }
            if (mod.MutatorCost <= 0)
            {
                throw new InvalidDataException($"{ModifiersFile}: '{mod.Id}' has no mutator cost");
            }
        }

        // Все типы узлов, кроме Empty.
        foreach (NodeType type in Enum.GetValues<NodeType>())
        {
            if (type != NodeType.Empty && !NodeTypes.ContainsKey(type))
            {
                throw new InvalidDataException($"{NodesFile}: node type '{type}' is not defined");
            }
        }

        // Карта: без датацентра нет цели забега.
        if (MapGen.Size.Datacenters < 1)
        {
            throw new InvalidDataException($"{MapGenFile}: at least one datacenter is required");
        }
        if (MapGen.Size.Clusters < 2 + MapGen.Size.Factories)
        {
            throw new InvalidDataException($"{MapGenFile}: not enough clusters for start, datacenter and factories");
        }
    }
}
