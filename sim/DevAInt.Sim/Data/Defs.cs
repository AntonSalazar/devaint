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
/// Общие числа правил забега.
/// </summary>
/// <param name="ActionPoints">Очков действия у daemon'а в ход.</param>
/// <param name="SlotLimit">Слотов эксплойтов у daemon'а.</param>
/// <param name="PatchMax">Максимальный <c>Patch</c> узла.</param>
/// <param name="MoveCost">Цена <c>Move</c> в очках действия.</param>
/// <param name="ExploitCost">Цена <c>Exploit</c> в очках действия.</param>
/// <param name="ScanCost">Цена <c>Scan</c> в очках действия.</param>
/// <param name="LurkCost">Цена <c>Lurk</c> в очках действия.</param>
/// <param name="NoiseDecay">Множитель затухания шума в начале хода.</param>
/// <param name="NoiseAudit">Порог шума, с которого узел вычищает аудит.</param>
/// <param name="ScanNoise">Шум от одного скана.</param>
/// <param name="StartCompute">Стартовый Compute.</param>
/// <param name="StartingExploits">Id стартовых эксплойтов daemon'а.</param>
/// <param name="DawnTurn">Ход, на котором наступает рассвет (поражение).</param>
/// <param name="ScorePerTurnLeft">Очков счёта за каждый ход до рассвета при победе.</param>
/// <param name="ScorePerNode">Очков счёта за каждый свой узел.</param>
/// <param name="MutatorBudget">Бюджет очков мутаторов на старте забега.</param>
public sealed record RulesDef(
    int ActionPoints, int SlotLimit, int PatchMax,
    int MoveCost, int ExploitCost, int ScanCost, int LurkCost,
    float NoiseDecay, float NoiseAudit, float ScanNoise,
    int StartCompute, string[] StartingExploits,
    int DawnTurn, int ScorePerTurnLeft, int ScorePerNode,
    int MutatorBudget);

// nodes.json

/// <summary>
/// Описание типа узла.
/// </summary>
/// <param name="Type">Тип узла.</param>
/// <param name="BasePatch">Базовая защита.</param>
/// <param name="Yield">Доход Compute в ход.</param>
/// <param name="ScanRadius">Радиус скана с этого узла.</param>
/// <param name="OsWeights">Вероятности ОС при генерации.</param>
public sealed record NodeTypeDef(
    NodeType Type, int BasePatch, float Yield, int ScanRadius, Dictionary<Os, float> OsWeights);

// exploits.json

/// <summary>
/// Описание эксплойта — карточки с числом.
/// </summary>
/// <param name="Id">Идентификатор.</param>
/// <param name="Name">Название для UI.</param>
/// <param name="TargetOs">По какой ОС бьёт.</param>
/// <param name="Power">Сила (тир 1..5).</param>
/// <param name="MaxCharges">Зарядов в слоте при полном.</param>
/// <param name="ComputeCost">Цена компиляции.</param>
/// <param name="ExposureLimit">Порог выгорания.</param>
/// <param name="NoisePerUse">Шум на целевом узле за применение.</param>
/// <param name="Rarity">Компилируется или только лут.</param>
public sealed record ExploitDef(
    string Id, string Name, Os TargetOs, int Power, int MaxCharges,
    int ComputeCost, int ExposureLimit, float NoisePerUse, Rarity Rarity);

// modifiers.json

/// <summary>
/// Мутатор — набор числовых коэффициентов, которые симуляция читает как есть.
/// Бонусы складываются, множители перемножаются (<c>01-RULES.md §10</c>).
/// </summary>
/// <param name="Id">Идентификатор.</param>
/// <param name="Name">Название для UI.</param>
/// <param name="Description">Описание для UI.</param>
/// <param name="Tags">Ветка: stealth, economy, offense, mobility, swarm.</param>
/// <param name="MutatorCost">Цена в очках бюджета на старте забега.</param>
/// <param name="PowerBonus">Бонус к Power эксплойтов по ОС.</param>
/// <param name="NoiseMult">Множитель шума от действий.</param>
/// <param name="ComputeMult">Множитель дохода.</param>
/// <param name="ExtraActionPoints">Бонус очков действия.</param>
/// <param name="ExtraCharges">Бонус к максимуму зарядов каждого слота.</param>
/// <param name="ExtraSlotLimit">Бонус к числу слотов.</param>
/// <param name="ScanRadiusBonus">Бонус к радиусу скана.</param>
/// <param name="StartingExploit">Id эксплойта, который выдаётся daemon'у на старте.</param>
public sealed record Modifier(
    string Id, string Name, string Description, string[] Tags, int MutatorCost,
    Dictionary<Os, int>? PowerBonus = null, float NoiseMult = 1f, float ComputeMult = 1f,
    int ExtraActionPoints = 0, int ExtraCharges = 0, int ExtraSlotLimit = 0,
    int ScanRadiusBonus = 0, string? StartingExploit = null);

// events.json

/// <summary>
/// Фаза расписания: с какого хода и как часто.
/// </summary>
/// <param name="FromTurn">С какого хода действует фаза.</param>
/// <param name="Every">Раз в сколько ходов срабатывает.</param>
public sealed record SchedulePhase(int FromTurn, int Every);

/// <summary>
/// Условие срабатывания мирового события.
/// </summary>
/// <param name="Kind">Вид условия.</param>
/// <param name="Schedule">Фазы расписания (для <see cref="TriggerKind.Schedule"/>).</param>
/// <param name="FromTurn">С какого хода возможно (для <see cref="TriggerKind.Chance"/>).</param>
/// <param name="Chance">Шанс за ход (для <see cref="TriggerKind.Chance"/>).</param>
public sealed record EventTrigger(TriggerKind Kind, SchedulePhase[]? Schedule = null, int FromTurn = 1, float Chance = 0f);

/// <summary>
/// Эффект мирового события.
/// </summary>
/// <param name="Kind">Вид эффекта.</param>
/// <param name="Amount">Величина (например, +Patch).</param>
/// <param name="Duration">Длительность в ходах (отключение линка).</param>
/// <param name="LimitMultiplier">Множитель порога выгорания после срабатывания.</param>
public sealed record EventEffect(EffectKind Kind, int Amount = 0, int Duration = 0, int LimitMultiplier = 1);

/// <summary>
/// Мировое событие: условие + эффект + текст для лога.
/// </summary>
/// <param name="Id">Идентификатор.</param>
/// <param name="Name">Название.</param>
/// <param name="Text">Текст для лога с плейсхолдерами.</param>
/// <param name="Trigger">Условие.</param>
/// <param name="Effect">Эффект.</param>
public sealed record WorldEventDef(string Id, string Name, string Text, EventTrigger Trigger, EventEffect Effect);

// mapgen.json

/// <summary>
/// Сколько дальних линков каждого вида.
/// </summary>
/// <param name="Backbone">Роутер ↔ роутер.</param>
/// <param name="Vpn">Сервер ↔ сервер.</param>
/// <param name="Sneakernet">Home ↔ Workstation.</param>
public sealed record LinkCounts(int Backbone, int Vpn, int Sneakernet);

/// <summary>
/// Размер и состав карты забега.
/// </summary>
/// <param name="Width">Ширина в гексах.</param>
/// <param name="Height">Высота в гексах.</param>
/// <param name="Clusters">Число кластеров.</param>
/// <param name="ExtraCorridors">Лишних коридоров сверх остовного дерева (обходные пути).</param>
/// <param name="Datacenters">Число датацентров (первый — цель забега).</param>
/// <param name="Factories">Число заводов.</param>
/// <param name="Links">Число дальних линков по видам.</param>
/// <param name="MinTargetDist">Минимальная дистанция от старта до цели.</param>
public sealed record MapSizeDef(
    int Width, int Height, int Clusters, int ExtraCorridors, int Datacenters, int Factories,
    LinkCounts Links, int MinTargetDist);

/// <summary>
/// Профиль кластера: из чего он состоит.
/// </summary>
/// <param name="Id">Идентификатор профиля.</param>
/// <param name="NodeWeights">Вероятности типов узлов.</param>
/// <param name="Center">Тип узла в центре (null — по весам).</param>
/// <param name="PatchBonus">Бонус к защите всех узлов кластера.</param>
/// <param name="NamePool">Пул имён для узлов.</param>
public sealed record ClusterProfile(
    string Id, Dictionary<NodeType, float> NodeWeights, NodeType? Center, int PatchBonus, string[] NamePool);

/// <summary>
/// Параметры генератора карты.
/// </summary>
/// <param name="ClusterRadiusMin">Минимальный радиус кластера.</param>
/// <param name="ClusterRadiusMax">Максимальный радиус кластера.</param>
/// <param name="HoleChance">Шанс дырки в кластере.</param>
/// <param name="MinCenterDist">Минимальная дистанция между центрами кластеров.</param>
/// <param name="Size">Размер и состав карты.</param>
/// <param name="Profiles">Профили кластеров.</param>
public sealed record MapGenDef(
    int ClusterRadiusMin, int ClusterRadiusMax, float HoleChance, int MinCenterDist,
    MapSizeDef Size, ClusterProfile[] Profiles);
