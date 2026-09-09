# Модель данных

> Единая точка правды о структурах. Код в `project/sim/`. Числа —
> в `07-BALANCE.md` и `project/data/*.json`.

## 1. Принципы

- **Определения (`*Def`) неизменяемы и грузятся из JSON.** Состояние партии
  ссылается на них по `Id` (строка), а не держит копии.
- **Состояние — плоское и сериализуемое.** Никаких ссылок на Godot-типы,
  никаких делегатов внутри состояния. `System.Text.Json` без кастомных
  конвертеров, кроме `Hex`.
- **Правила читают данные, а не ветвятся по Id.** Если в коде появляется
  `if (def.Id == "zero_day_cache")` — это баг: нужен новый числовой
  эффект в `Modifier`.
- **Записи (`record`) для определений, классы для состояния.** Состояние
  меняется на месте (`Apply`), снапшот — явное `Clone()`.

## 2. Геометрия

```csharp
public readonly record struct Hex(int Q, int R)
{
    public static readonly Hex[] Directions = { (1,0), (1,-1), (0,-1), (-1,0), (-1,1), (0,1) };
    public Hex Neighbor(int dir);
    public int DistanceTo(Hex other);            // через кубические координаты
    public (int Col, int Row) ToOffset();        // odd-r, для TileMapLayer
    public static Hex FromOffset(int col, int row);
}

public sealed class Layer<T>
{
    public int Width, Height;
    private readonly T[] _cells;                 // индекс = row * Width + col
    public T this[Hex h] { get; set; }
    public bool Contains(Hex h);
    public ReadOnlySpan<T> Raw { get; }          // для упаковки в текстуру и сериализации
}
```

## 3. Определения (JSON → record)

Все определения — `sealed record` с позиционными параметрами, имена
полей 1:1 с ключами JSON (camelCase в файле, PascalCase в коде —
`PropertyNameCaseInsensitive`). Enum-ы — строками (`JsonStringEnumConverter`).
Таблицы «по числу фракций» — списки записей с полем `Factions`, по одной
на каждый N от 2 до `MaxFactions` (без диапазонов — явно и проверяемо).

```csharp
public enum NodeType { Empty, Home, Workstation, Server, Router, IoT, Controller }
public enum Os { Kestrel, Bastion, Mote, Forge }
public enum LinkKind { Backbone, Vpn, Sneakernet }
public enum Rarity { Common, ZeroDay }
public enum TriggerKind { EveryTurn, Schedule, Chance, ExposureLimit }
public enum EffectKind { PatchWave, Audit, Burnout, LinkOutage, Migration }

// rules.json
public sealed record VictoryDef(int Factions, float DominationShare, int TurnLimit);
public sealed record RulesDef(
    int MaxFactions, int ActionPoints, int SlotLimit, int SpawnCost, int HardenCost, int HardenMax,
    float NoiseDecay, float NoiseVisible, float NoiseAudit, float ScanNoise, float HardenNoise,
    int StartCompute, int LateStartBonus, string[] StartingExploits,
    VictoryDef[] Victory, bool RequireAllRouters, bool SharedVision, bool AlliesAdjacent, int MutatorBudget);

// nodes.json
public sealed record NodeTypeDef(
    NodeType Type, int BasePatch, float Yield, int ScanRadius, float AiValue, Dictionary<Os, float> OsWeights);

// exploits.json
public sealed record ExploitDef(
    string Id, string Name, Os TargetOs, int Power, int MaxCharges,
    int ComputeCost, int ExposureLimit, float NoisePerUse, Rarity Rarity);

// modifiers.json — одна структура для мутатора и гена
public sealed record Modifier(
    string Id, string Name, string Description, string[] Tags,
    int MutatorCost, int ComputeCost, string[] Requires,
    Dictionary<Os, int>? PowerBonus = null, float NoiseMult = 1f, float ComputeMult = 1f,
    int ExtraActionPoints = 0, int ExtraCharges = 0, int ExtraDaemonSlots = 0,
    int ExtraSlotLimit = 0, int HardenBonus = 0, int ScanRadiusBonus = 0,
    string? StartingExploit = null);

// events.json
public sealed record SchedulePhase(int FromTurn, int Every);
public sealed record EventTrigger(TriggerKind Kind, SchedulePhase[]? Schedule = null, int FromTurn = 1, float Chance = 0f);
public sealed record EventEffect(EffectKind Kind, int Amount = 0, int Duration = 0, int LimitMultiplier = 1);
public sealed record WorldEventDef(string Id, string Name, string Text, EventTrigger Trigger, EventEffect Effect);

// mapgen.json
public sealed record LinkCounts(int Backbone, int Vpn, int Sneakernet);
public sealed record MapSizeDef(
    int Factions, int Width, int Height, int Clusters, int ExtraCorridors, int Datacenters, int Factories,
    LinkCounts Links, int MinStartDist);
public sealed record ClusterProfile(
    string Id, Dictionary<NodeType, float> NodeWeights, NodeType? Center, int PatchBonus, string[] NamePool);
public sealed record MapGenDef(
    int ClusterRadiusMin, int ClusterRadiusMax, float HoleChance, int MinCenterDist, bool Mirror,
    MapSizeDef[] Sizes, ClusterProfile[] Profiles);

// ai.json, factions.json
public sealed record AiProfileDef(string Id, bool UseSpecialRules, Dictionary<string, float> Weights);
public sealed record FactionPresetDef(string Id, string Name, string Color, string[] Mutators, string AiProfile, string[] Voice);
```

Всё это собирается в один неизменяемый `Rules`:

```csharp
public sealed class Rules
{
    public RulesDef Core { get; }
    public IReadOnlyDictionary<NodeType, NodeTypeDef> NodeTypes { get; }
    public IReadOnlyDictionary<string, ExploitDef> Exploits { get; }
    public IReadOnlyDictionary<string, Modifier> Modifiers { get; }
    public IReadOnlyList<WorldEventDef> Events { get; }
    public MapGenDef MapGen { get; }                                   // + MapGen.Profiles как словарь по Id
    public IReadOnlyDictionary<string, AiProfileDef> AiProfiles { get; }
    public IReadOnlyDictionary<string, FactionPresetDef> Presets { get; }

    public static Rules Load(string dir);          // читает все файлы, валидирует, бросает InvalidDataException
    public VictoryDef VictoryFor(int factions);    // вне 2..MaxFactions → ArgumentOutOfRangeException
    public MapSizeDef MapSizeFor(int factions);
}
```

`Rules.Load("res://data")` — из Godot (через `ProjectSettings.GlobalizePath`),
`Rules.Load(path)` — из тестов и headless. Загрузка **валидирует** (§7)
и падает `InvalidDataException` с текстом, в котором есть имя файла
и виновный `Id`.

## 4. Состояние партии

```csharp
public sealed class World
{
    public Layer<NodeType> Type;  public Layer<Os> Os;
    public Layer<int> Patch;      public Layer<int> Hardening;
    public Layer<int> Owner;      // -1 нейтрал
    public Layer<float> Noise;
    public Layer<bool>[] KnownBy; // индекс = FactionId
    public Layer<string> Name;
    public List<Link> Links;      // Link(Hex A, Hex B, LinkKind Kind, int DisabledUntilTurn)
}

public sealed class Faction
{
    public int Id; public string Name; public string Color;
    public List<string> Mutators; public List<string> Genome;   // Id модификаторов
    public int Compute; public Controller Controller;           // Human | Ai(profile) | Remote(peer)
    public int Team;                                            // союзники — одинаковый Team
    public bool Eliminated;
    public List<string> Arsenal;                                // эксплойты на серверах, ещё не в слотах
    public FactionStats Stats;                                  // кэш свёртки модификаторов
}

public sealed class Daemon
{
    public int Id; public int FactionId; public string Name;
    public Hex Pos; public int ActionPoints;
    public List<ExploitSlot> Exploits;                          // ExploitSlot(string DefId, int Charges)
}

public sealed class GameState
{
    public int Seed; public int Turn; public int ActiveFaction;
    public List<int> TurnOrder;                                 // Id фракций в порядке хода
    public World World; public List<Faction> Factions; public List<Daemon> Daemons;
    public Dictionary<string, int> Exposure;                    // DefId → применений с последнего выгорания
    public Dictionary<string, int> ExposureLimit;               // DefId → текущий порог
    public Dictionary<string, int> PowerPenalty;                // DefId → накопленное −Power от выгораний
    public int NextDaemonId;
    public GameState Clone();
}
```

`FactionStats` — свёртка: `PowerBonus[Os]`, `NoiseMult`, `ComputeMult`,
`ActionPoints`, `SlotLimit`, `ExtraCharges`, `DaemonLimitBonus`,
`HardenBonus`, `ScanRadius`. Пересчитывается при `Mutate`.

## 5. Действия и события

```csharp
public abstract record Action(int FactionId);
public sealed record Move(int FactionId, int DaemonId, Hex To) : Action(FactionId);
public sealed record Exploit(int FactionId, int DaemonId, Hex Target, int SlotIx) : Action(FactionId);
public sealed record Scan(int FactionId, int DaemonId) : Action(FactionId);
public sealed record Harden(int FactionId, int DaemonId) : Action(FactionId);
public sealed record Lurk(int FactionId, int DaemonId) : Action(FactionId);
public sealed record Compile(int FactionId, Hex Server, string ExploitId, int? DaemonId) : Action(FactionId);
public sealed record Spawn(int FactionId, Hex Server) : Action(FactionId);
public sealed record Mutate(int FactionId, string ModifierId) : Action(FactionId);
public sealed record EndTurn(int FactionId) : Action(FactionId);

public abstract record SimEvent(int Turn);
// NodeCaptured, CaptureFailed(reason), DaemonSpawned, DaemonKilled(cause),
// ExploitCompiled, ExploitBurned, GenomeChanged, WorldEventFired(id),
// TurnStarted(faction), GameOver(winner, condition)
```

API симуляции:

```csharp
public static class Sim
{
    public static GameState NewGame(Rules rules, int seed, FactionSetup[] setups);
    public static IReadOnlyList<Action> Legal(Rules rules, GameState s, int factionId);
    public static Verdict Check(Rules rules, GameState s, Action a);      // Ok | Illegal(reason)
    public static IReadOnlyList<SimEvent> Apply(Rules rules, GameState s, Action a, Rng rng);
    public static CaptureCheck CanCapture(Rules rules, GameState s, Daemon h, int slotIx, Hex target);
}
```

`CanCapture` возвращает не bool, а разбор для UI: нужная сила, имеющаяся
сила, ОС совпала ли, актуальны ли данные (`KnownAtTurn`).

## 6. Файлы данных

| Файл | Содержимое |
|---|---|
| `data/rules.json` | `RulesDef` — общие числа и таблица победы по N |
| `data/nodes.json` | `NodeTypeDef[]` — 6 типов (без Empty) |
| `data/exploits.json` | `ExploitDef[]` — 18 common (Forge без T1–T2) + 4 зеро-дея |
| `data/modifiers.json` | `Modifier[]` — 15 записей |
| `data/events.json` | `WorldEventDef[]` — 5 событий |
| `data/mapgen.json` | `MapGenDef` — размеры по N, профили кластеров с пулами имён |
| `data/ai.json` | `AiProfileDef[]` — 5 профилей |
| `data/factions.json` | `FactionPresetDef[]` — 4 пресета |

Пример `exploits.json`:

```json
[
  { "id": "kestrel_t1", "name": "Phish kit", "targetOs": "Kestrel", "power": 1,
    "maxCharges": 4, "computeCost": 4, "exposureLimit": 40, "noisePerUse": 0.25,
    "rarity": "Common" },
  { "id": "bastion_t3", "name": "Priv-esc chain", "targetOs": "Bastion", "power": 3,
    "maxCharges": 2, "computeCost": 22, "exposureLimit": 20, "noisePerUse": 0.35,
    "rarity": "Common" }
]
```

## 7. Инварианты и валидация (тесты на данных)

- Все `Id` уникальны; все `Requires`, `StartingExploit`, `Mutators`
  пресетов, `AiProfile` ссылаются на существующие записи.
- Для каждой ОС и тира из `07-BALANCE.md §3` есть ровно один `Common`
  эксплойт (кроме объявленных исключений `Forge` T1–T2).
- Все `NodeType`, кроме `Empty`, определены; `NodeWeights` профилей
  кластеров суммируются в 1; у профиля с `Center` он не `Empty`.
- `OsWeights` каждого типа узла суммируются в 1.
- Сумма `MutatorCost` лучшего пресета ≤ `MutatorBudget`.
- `DominationShare`, `TurnLimit` и `mapgen` заданы для каждого N
  от 2 до `MaxFactions`.
- В `FactionSetup[]` партии: 2 ≤ длина ≤ `MaxFactions`, хотя бы две
  разные команды, цвета не повторяются.
- `Requires` без циклов.
- `Hex` round-trip: `FromOffset(ToOffset(h)) == h` на всей карте;
  `DistanceTo` симметрична и удовлетворяет неравенству треугольника
  (выборочно).
- Сериализация: `Clone()` и JSON round-trip дают равное состояние.

## 8. Расширение без правок кода (проверка принципа)

| Хочу | Делаю |
|---|---|
| Новый тип узла | Строка в `nodes.json` + глиф в тайлсете + профиль генератора |
| Новый мутатор «−1 к цене компиляции» | Новое числовое поле в `Modifier` + чтение в `Compile` — **один** код-патч, дальше только данные |
| Новое мировое событие того же вида | Строка в `events.json` |
| Событие нового вида | Новый `EventEffect.Kind` + его `Apply` — код-патч |
| 3–8 фракций | Уже поддержано: `FactionSetup[]` длиннее, `KnownBy` — массив, `TurnOrder` — список; только таблицы `DominationShare`/`TurnLimit`/`mapgen` по N |
| Альянс на старте | `Team` в `FactionSetup` — уже поддержано |
| Динамическая дипломатия | Новые `Action` (`ProposeAlliance`, `AcceptAlliance`, `BreakAlliance`) + правило штрафа — код-патч, структура не меняется |
| Онлайн | `Controller.Remote` + транспорт в Godot-слое; `Sim` не меняется |
| Сценарий | `seed` + список правок слоёв + свои `events`/`voice` |
