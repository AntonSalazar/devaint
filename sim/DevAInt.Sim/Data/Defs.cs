using System.Collections.Generic;

namespace DevAInt.Sim.Data;


/// <summary>
/// Типы узлов.
/// </summary>
public enum NodeType
{
    /// <summary>
    /// Воздушный зазор, непроходим, не захватывается.
    /// </summary>
    Empty,

    /// <summary>
    /// Домашний ПК. Дешёвый, слабый, их много.
    /// </summary>
    Home,

    /// <summary>
    /// Офисная машина. Средний доход, средняя защита.
    /// </summary>
    Workstation,

    /// <summary>
    /// "Город". В нём есть доход, компиляция эксплоитов, спавн Daemon, пополнение зарядов.
    /// </summary>
    Server,

    /// <summary>
    /// Мост между кластерами. Единственный проход.
    /// </summary>
    Router,

    /// <summary>
    /// Умные устройства. Почти без защиты, почти без дохода, но их много и они рядом со всем.
    /// </summary>
    IoT,

    /// <summary>
    /// Промышленный контроллер. Высокая защита, высокий доход, редкие OS.
    /// </summary>
    Controller
}

/// <summary>
/// Семейства OS.
/// </summary>
public enum Os
{
    /// <summary>
    /// Массовая потребительская. Много эксплойтов, быстро патчится.
    /// Живет в <see cref="NodeType.Home"/>, <see cref="NodeType.Workstation"/>.
    /// </summary>
    Kestrel,

    /// <summary>
    /// Серверная. Мало эксплойтов, высокая базовая защита.
    /// Живет в <see cref="NodeType.Server"/>, <see cref="NodeType.Router"/>.
    /// </summary>
    Bastion,

    /// <summary>
    /// Встроенная. Дырявая, почти не патчится.
    /// Живет в <see cref="NodeType.IoT"/>.
    /// </summary>
    Mote,

    /// <summary>
    /// Промышленная. Редкие дорогие эксплойты, патчей почти нет.
    /// Живет в <see cref="NodeType.Controller"/>.
    /// </summary>
    Forge
}

/// <summary>
/// Дальние линки.
/// Двусторонняя связь между не соседними гексами.
/// Для всех правил концы линка считаются соседями.
/// По ним можно захватывать, двигаться, сканировать так же, как по шести соседним гексам <see cref="Hex"/>.
/// </summary>
public enum LinkKind
{
    /// <summary>
    /// Быстрый прорыв через полкарты.
    /// Магистраль между <see cref="NodeType.Router"/>.
    /// </summary>
    Backbone,

    /// <summary>
    /// Обход роутера.
    /// Связь между <see cref="NodeType.Workstation"/>.
    /// </summary>
    Vpn,

    /// <summary>
    /// Прыжок из жилого района прямо в офис.
    /// Связь между <see cref="NodeType.Home"/> и <see cref="NodeType.Workstation"/>.
    /// </summary>
    Sneakernet
}


/// <summary>
/// Редкость эксплоита.
/// </summary>
public enum Rarity
{
    /// <summary>
    /// Обычный: компилируется на своём сервере за Compute.
    /// </summary>
    Common,

    /// <summary>
    /// 0Day: не компилируется, только выдается либо как мутатором, либо как лут за ценный узел.
    /// </summary>
    ZeroDay
}

/// <summary>
/// Когда событие произойдет.
/// </summary>
public enum TriggerKind
{
    /// <summary>
    /// Каждый ход.
    /// </summary>
    EveryTurn,

    /// <summary>
    /// По расписанию из фаз <see cref="SchedulePhase"/>.
    /// </summary>
    Schedule,

    /// <summary>
    /// С хода FromTurn, каждый ход с вероятностью Chance.
    /// </summary>
    Chance,

    /// <summary>
    /// Когда счётчик применения какого-то эксплойта достиг порога.
    /// </summary>
    ExposureLimit
}

/// <summary>
/// Список событий.
/// </summary>
public enum EffectKind
{
    /// <summary>
    /// Случайная OS: Patch + 1 всем узлам этой OS (кап 5), включая захваченные.
    /// </summary>
    PatchWave,

    /// <summary>
    /// Все узлы с Noise больше-равно 0.8f: владелец снимается,
    /// Hardering становится 0, Patch + 1, Daemon на узле убит.
    /// </summary>
    Audit,

    /// <summary>
    /// Power - 1 эксплойту для всех, лог "уязвимость закрыта".
    /// </summary>
    Burnout,

    /// <summary>
    /// Случайный линк отключен на 3 хода.
    /// </summary>
    LinkOutage,

    /// <summary>
    /// Нейтральный сервер меняет OS.
    /// </summary>
    Migration
}

// rules.json

/// <summary>
/// Условия победы.
/// </summary>
/// <param name="Factions">Сколько фракций.</param>
/// <param name="DominationShare">Процент доминирования для победы.</param>
/// <param name="TurnLimit">Максимальное количество ходов.</param>
public sealed record VictoryDef(int Factions, float DominationShare, int TurnLimit);

/// <summary>
/// Общие правила игры.
/// </summary>
/// <param name="MaxFactions">Верхняя граница числа фракций.</param>
/// <param name="ActionPoints">Очков действия за ход.</param>
/// <param name="SlotLimit">Количество слотов эксплойтов у Daemon.</param>
/// <param name="SpawnCost">Цена спавна нового Daemon на сервере.</param>
/// <param name="HardenCost">Цена одного уровня укрепления.</param>
/// <param name="HardenMax">Потолок уровня укрепления.</param>
/// <param name="NoiseDecay">Во сколько раз шум затухает каждый ход.</param>
/// <param name="NoiseVisible">С какого уровня узел виден всем сквозь туман.</param>
/// <param name="NoiseAudit">С какого уровня узел попадает под аудит.</param>
/// <param name="ScanNoise">Сколько шума добавляет скан.</param>
/// <param name="HardenNoise">Сколько шума добавляет укрепление.</param>
/// <param name="StartCompute">Стартовый ресурс.</param>
/// <param name="LateStartBonus">Компенсация тем, кто ходит позже: второй получает +3, третий +6 и т.д</param>
/// <param name="StartingExploits">id эксплойтов, с которыми появляется первый Daemon.</param>
/// <param name="Victory">Массив условий побед.</param>
/// <param name="RequireAllRouters">Нужны ли доминирования ещё и все роутеры.</param>
/// <param name="SharedVision">Видят ли союзники общий туман.</param>
/// <param name="AlliesAdjacent">Ставить ли союзников рядом при генерации.</param>
/// <param name="MutatorBudget">Сколько очков на мутаторы при создании фракции.</param>
public sealed record RulesDef(
    int MaxFactions, int ActionPoints, int SlotLimit, int SpawnCost, int HardenCost, int HardenMax,
    float NoiseDecay, float NoiseVisible, float NoiseAudit, float ScanNoise, float HardenNoise,
    int StartCompute, int LateStartBonus, string[] StartingExploits,
    VictoryDef[] Victory, bool RequireAllRouters, bool SharedVision, bool AlliesAdjacent, int MutatorBudget);


// nodes.json

/// <summary>
/// "Паспорт типа" узла.
/// Что за машина стоит на гексе и как она себя ведет.
/// </summary>
/// <param name="Type">Какой тип.</param>
/// <param name="BasePatch">Защита "из коробки".</param>
/// <param name="Yield">Сколько Compute узел приносит владельцу в доход.</param>
/// <param name="ScanRadius">Радиус скана, если Daemon стоит на этом узле.</param>
/// <param name="AiValue">Насколько бот хочет этот узел. Множитель для оценки действий.</param>
/// <param name="OsWeights">Какие OS бывают у этого типа и с какой вероятностью.</param>
public sealed record NodeTypeDef(
    NodeType Type, int BasePatch, float Yield, int ScanRadius, float AiValue, Dictionary<Os, float> OsWeights);


// exploits.json

/// <summary>
/// "Карточка" эксплоита.
/// Что он ломает, насколько сильно, сколько раз, какой ценой и т.п.
/// </summary>
/// <param name="Id">Ключ для ссылок из других данных и из состояния партии.</param>
/// <param name="Name">Название для игрока.</param>
/// <param name="TargetOs">Единственная OS, по которой работает.</param>
/// <param name="Power">Сила = тир.</param>
/// <param name="MaxCharges">Сколько зарядов в одном слоте. Пополняется на своём сервере по +1 в ход.</param>
/// <param name="ComputeCost">Цена компиляции на сервере.</param>
/// <param name="ExposureLimit">Сколько применений (всеми фракциями вместе) до "выгорания".</param>
/// <param name="NoisePerUse">Сколько шума оставляет одно применение на целевом узле.</param>
/// <param name="Rarity">Редкость по <see cref="Rarity"/>.</param>
public sealed record ExploitDef(
    string Id, string Name, Os TargetOs, int Power, int MaxCharges,
    int ComputeCost, int ExposureLimit, float NoisePerUse, Rarity Rarity);

// modifiers.json — одна структура для мутатора и гена

/// <summary>
/// Является и мутатором и геном.
/// </summary>
/// <param name="Id">Ключ для ссылок.</param>
/// <param name="Name">Название для игрока.</param>
/// <param name="Description">Описание для игрока.</param>
/// <param name="Tags">
/// Ветка: `stealth`, `economy`, `offense`, `mobility`, `swarm`.
/// По тегу UI группирует карточки и красит.
/// </param>
/// <param name="MutatorCost">Цена в очках при создании фракции. 0 = как мутатор не доступен.</param>
/// <param name="ComputeCost">Цена в партии. 0 = как ген недоступен.</param>
/// <param name="Requires">id других модификаторов, которые нужно иметь раньше. Из этого рисуется дерево.</param>
/// <param name="PowerBonus">+N к Power экспойтов по целевой OS.</param>
/// <param name="NoiseMult">Множитель шума от действий.</param>
/// <param name="ComputeMult">Множитель дохода.</param>
/// <param name="ExtraActionPoints">+N очков действия для Daemon.</param>
/// <param name="ExtraCharges">+N к максимуму зарядов каждого слота.</param>
/// <param name="ExtraDaemonSlots">+N к лимиту Daemon.</param>
/// <param name="ExtraSlotLimit">+N к слоту эксплойтов для Daemon.</param>
/// <param name="HardenBonus">+N к укреплению.</param>
/// <param name="ScanRadiusBonus">+N к радиусу скана.</param>
/// <param name="StartingExploit">id эксплойта для первого Daemon (только мутатор).</param>
public sealed record Modifier(
    string Id, string Name, string Description, string[] Tags,
    int MutatorCost, int ComputeCost, string[] Requires,
    Dictionary<Os, int>? PowerBonus = null, float NoiseMult = 1f, float ComputeMult = 1f,
    int ExtraActionPoints = 0, int ExtraCharges = 0, int ExtraDaemonSlots = 0,
    int ExtraSlotLimit = 0, int HardenBonus = 0, int ScanRadiusBonus = 0,
    string? StartingExploit = null);

// events.json

/// <summary>
/// Одна фаза расписания.
/// </summary>
/// <param name="FromTurn">Начиная с хода N.</param>
/// <param name="Every">Каждый ход K.</param>
public sealed record SchedulePhase(int FromTurn, int Every);

/// <summary>
/// Тригер события, когда оно срабатывает.
/// </summary>
/// <param name="Kind">Что читается.</param>
/// <param name="Schedule">Массив фаз.</param>
/// <param name="FromTurn">С какого хода.</param>
/// <param name="Chance">С каким шансом.</param>
public sealed record EventTrigger(TriggerKind Kind, SchedulePhase[]? Schedule = null, int FromTurn = 1, float Chance = 0f);

/// <summary>
/// Эффект события.
/// Что именно применяется.
/// </summary>
/// <param name="Kind">Что читается.</param>
/// <param name="Amount">На сколько.</param>
/// <param name="Duration">Длительность в количестве ходов.</param>
/// <param name="LimitMultiplier">Множитель ограничения.</param>
public sealed record EventEffect(EffectKind Kind, int Amount = 0, int Duration = 0, int LimitMultiplier = 1);

/// <summary>
/// Событие мировой фазы.
/// </summary>
/// <param name="Id">Ключ.</param>
/// <param name="Name">Короткое имя для HUD.</param>
/// <param name="Text">Строка для лога с плейсхолдерами в фигурных скобках.</param>
/// <param name="Trigger">Когда срабатывает.</param>
/// <param name="Effect">Что делает.</param>
public sealed record WorldEventDef(string Id, string Name, string Text, EventTrigger Trigger, EventEffect Effect);

// mapgen.json

/// <summary>
/// Сколько дальних линков.
/// Просто 3 счётчика, сгруппированные в одну структуру.
/// </summary>
/// <param name="Backbone">Между самыми дальными роутерами.</param>
/// <param name="Vpn">Между офисами.</param>
/// <param name="Sneakernet">Между домами и офисами.</param>
public sealed record LinkCounts(int Backbone, int Vpn, int Sneakernet);

/// <summary>
/// Параметры карты для N фракций.
/// </summary>
/// <param name="Factions">Для партий на сколько факций эта строка.</param>
/// <param name="Width">Размер карты в гексах в ширину.</param>
/// <param name="Height">Размер карты в гексах в высоту.</param>
/// <param name="Clusters">Сколько всего кластеров.</param>
/// <param name="Datacenters">Сколько из них - датацентры (главные призы).</param>
/// <param name="Factories">Сколько заводов.</param>
/// <param name="Links">Сколько дальних линков.</param>
/// <param name="MinStartDist">Минимальная дистанция между стартами в гексах.</param>
/// <param name="ExtraCorridors">Дополнительные коридоры между кластерами.</param>
public sealed record MapSizeDef(
    int Factions, int Width, int Height, int Clusters, int Datacenters, int Factories,
    LinkCounts Links, int MinStartDist, int ExtraCorridors);

/// <summary>
/// Описание кластера.
/// </summary>
/// <param name="Id">Ключ. Например: "datacenter", "office" и т.п</param>
/// <param name="NodeWeights">Какие типы узлов и с какой вероятностью заливаются в кластер.</param>
/// <param name="Center">Какой узел ставится в центр кластера. Если null - обычный из <paramref name="NodeWeights"/>.</param>
/// <param name="PatchBonus">Добавка к базовой защите всех узлов кластера.</param>
/// <param name="NamePool">Из чего генератор собирает имена узлов.</param>
public sealed record ClusterProfile(
    string Id, Dictionary<NodeType, float> NodeWeights, NodeType? Center, int PatchBonus, string[] NamePool);

/// <summary>
/// Корень генерации карты.
/// </summary>
/// <param name="ClusterRadiusMin">Минимальный радиус заливки кластера.</param>
/// <param name="ClusterRadiusMax">Максимальный радиус заливки кластера.</param>
/// <param name="HoleChance">Шанс, что гекс внутри радиуса останется пустым, чтобы кластеры не были ровными шестиугольниками.</param>
/// <param name="MinCenterDist">Минимальная дистанция между центрами кластеров.</param>
/// <param name="Mirror">Зеркальная карта (только для 2 и 4х фракций).</param>
/// <param name="Sizes">Таблица по числу фракций.</param>
/// <param name="Profiles">Профили кластеров.</param>
public sealed record MapGenDef(
    int ClusterRadiusMin, int ClusterRadiusMax, float HoleChance, int MinCenterDist, bool Mirror,
    MapSizeDef[] Sizes, ClusterProfile[] Profiles);

// ai.json, factions.json

/// <summary>
/// Описание AI-бота.
/// Бот оценивает каждое возможное действие числом и берет лучшее.
/// Формула оценки одна на всех, а веса - это данные.
/// </summary>
/// <param name="Id">Ключ, например "balanced".</param>
/// <param name="UseSpecialRules">
/// Включены ли "правила здравого смысла".
/// Например не оставаться на шумном узле, не тратить последний Т4+ на мелочь и т.п
/// </param>
/// <param name="Weights">Веса оценки действий.</param>
public sealed record AiProfileDef(string Id, bool UseSpecialRules, Dictionary<string, float> Weights);

/// <summary>
/// Готовая фракция.
/// Пресет, игрок выбирает его на экране создания партии.
/// Бот возьмет как есть.
/// </summary>
/// <param name="Id">Ключ, например "sentinel".</param>
/// <param name="Name">Название для игрока.</param>
/// <param name="Color">Цвет в hex-форме.</param>
/// <param name="Mutators">id модификаторов на бюджет.</param>
/// <param name="AiProfile">Какой характер для бота.</param>
/// <param name="Voice">Реплики для лога событий.</param>
public sealed record FactionPresetDef(string Id, string Name, string Color, string[] Mutators, string AiProfile, string[] Voice);
