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

```csharp
public enum NodeType { Empty, Home, Workstation, Server, Router, IoT, Controller }
public enum Os { Kestrel, Bastion, Mote, Forge }
public enum LinkKind { Backbone, Vpn, Sneakernet }
public enum Rarity { Common, ZeroDay }

public sealed record NodeTypeDef(
    NodeType Type, int BasePatch, float Yield, int ScanRadius, float AiValue,
    Dictionary<Os, float> OsWeights, string[] NamePool);

public sealed record ExploitDef(
    string Id, string Name, Os TargetOs, int Power, int MaxCharges,
    int ComputeCost, int ExposureLimit, float NoisePerUse, Rarity Rarity);

public sealed record Modifier(
    string Id, string Name, string Description, string[] Tags,
    int MutatorCost, int ComputeCost, string[] Requires,
    Dictionary<Os, int>? PowerBonus, float NoiseMult = 1f, float ComputeMult = 1f,
    int ExtraActionPoints = 0, int ExtraCharges = 0, int ExtraHeroSlots = 0,
    int ExtraSlotLimit = 0, int HardenBonus = 0, int ScanRadiusBonus = 0,
    string? StartingExploit = null);

public sealed record WorldEventDef(
    string Id, string Name, string Text,
    EventTrigger Trigger,        // { Kind: EveryNTurns | Chance | ExposureLimit, Params… }
    EventEffect Effect);         // { Kind: PatchWave | Audit | Burnout | LinkOutage | Migration, Params… }

public sealed record MapGenDef(
    int Width, int Height, int Clusters, int ClusterRadiusMin, int ClusterRadiusMax,
    float HoleChance, int MinCenterDist, int MinStartDist,
    Dictionary<string, ClusterProfile> Profiles, LinkCounts Links, bool Mirror);

public sealed record RulesDef(
    int MaxFactions,                                         // 8
    int ActionPoints, int SlotLimit, int SpawnCost, int HardenCost, int HardenMax,
    float NoiseDecay, float NoiseVisible, float NoiseAudit,
    int StartCompute, int LateStartBonus, string[] StartingExploits,
    Dictionary<int, float> DominationShare,                  // по числу фракций
    Dictionary<int, int> TurnLimit,                          // по числу фракций
    bool SharedVision, bool AlliesAdjacent, int MutatorBudget);

public enum ControllerKind { Human, Ai, Remote }
public sealed record Controller(ControllerKind Kind, string? AiProfile = null, string? PeerId = null);

public sealed record FactionSetup(string Name, string Color, string[] Mutators, Controller Controller, int Team);

public sealed record AiProfileDef(string Id, Dictionary<string, float> Weights, bool UseSpecialRules);

public sealed record FactionPresetDef(string Id, string Name, string Color, string[] Mutators, string AiProfile, string[] Voice);
```

Всё это собирается в один неизменяемый `Rules` при старте:
`Rules.Load("res://data")` в Godot / `Rules.Load(path)` в headless.
Загрузка **валидирует** (§7) и падает с понятной ошибкой.

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

public sealed class Hero
{
    public int Id; public int FactionId; public string Name;
    public Hex Pos; public int ActionPoints;
    public List<ExploitSlot> Exploits;                          // ExploitSlot(string DefId, int Charges)
}

public sealed class GameState
{
    public int Seed; public int Turn; public int ActiveFaction;
    public List<int> TurnOrder;                                 // Id фракций в порядке хода
    public World World; public List<Faction> Factions; public List<Hero> Heroes;
    public Dictionary<string, int> Exposure;                    // DefId → применений с последнего выгорания
    public Dictionary<string, int> ExposureLimit;               // DefId → текущий порог
    public Dictionary<string, int> PowerPenalty;                // DefId → накопленное −Power от выгораний
    public int NextHeroId;
    public GameState Clone();
}
```

`FactionStats` — свёртка: `PowerBonus[Os]`, `NoiseMult`, `ComputeMult`,
`ActionPoints`, `SlotLimit`, `ExtraCharges`, `HeroLimitBonus`,
`HardenBonus`, `ScanRadius`. Пересчитывается при `Mutate`.

## 5. Действия и события

```csharp
public abstract record Action(int FactionId);
public sealed record Move(int FactionId, int HeroId, Hex To) : Action(FactionId);
public sealed record Exploit(int FactionId, int HeroId, Hex Target, int SlotIx) : Action(FactionId);
public sealed record Scan(int FactionId, int HeroId) : Action(FactionId);
public sealed record Harden(int FactionId, int HeroId) : Action(FactionId);
public sealed record Lurk(int FactionId, int HeroId) : Action(FactionId);
public sealed record Compile(int FactionId, Hex Server, string ExploitId, int? HeroId) : Action(FactionId);
public sealed record Spawn(int FactionId, Hex Server) : Action(FactionId);
public sealed record Mutate(int FactionId, string ModifierId) : Action(FactionId);
public sealed record EndTurn(int FactionId) : Action(FactionId);

public abstract record SimEvent(int Turn);
// NodeCaptured, CaptureFailed(reason), HeroSpawned, HeroKilled(cause),
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
    public static CaptureCheck CanCapture(Rules rules, GameState s, Hero h, int slotIx, Hex target);
}
```

`CanCapture` возвращает не bool, а разбор для UI: нужная сила, имеющаяся
сила, ОС совпала ли, актуальны ли данные (`KnownAtTurn`).

## 6. Файлы данных

| Файл | Содержимое |
|---|---|
| `data/rules.json` | `RulesDef` — общие числа |
| `data/nodes.json` | `NodeTypeDef[]` |
| `data/exploits.json` | `ExploitDef[]` (20 common + зеро-деи) |
| `data/modifiers.json` | `Modifier[]` |
| `data/events.json` | `WorldEventDef[]` |
| `data/mapgen.json` | `MapGenDef` |
| `data/ai.json` | `AiProfileDef[]` |
| `data/factions.json` | `FactionPresetDef[]` |
| `data/names/*.json` | Пулы имён по профилям кластеров |

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
