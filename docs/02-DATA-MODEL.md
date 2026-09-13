# Модель данных

> Единая точка правды о структурах. Код — в `sim/DevAInt.Sim/`. Числа —
> в `03-BALANCE.md` и `project/data/*.json`.

## 1. Принципы

- **Определения (`*Def`) неизменяемы и грузятся из JSON.** Состояние
  ссылается на них по `Id` (строка), а не держит копии.
- **Состояние — плоское и сериализуемое.** Никаких ссылок на Godot-типы,
  никаких делегатов. `System.Text.Json` без кастомных конвертеров,
  кроме `Hex`.
- **Правила читают данные, а не ветвятся по Id.** `if (def.Id == "…")`
  в коде — баг: нужен новый числовой эффект в `Modifier`.
- **Записи (`record`) для определений, действий и событий; классы —
  для состояния.** Состояние меняется на месте (`Apply`), снапшот —
  явное `Clone()`.
- **Числа в коде запрещены.** Любой коэффициент — из загруженных таблиц.

## 2. Геометрия (есть, без изменений)

```csharp
public readonly record struct Hex(int Q, int R)
{
    public static readonly Hex[] Directions;
    public Hex Neighbor(int dir);
    public int DistanceTo(Hex other);
    public IEnumerable<Hex> LineTo(Hex other);
    public (int Col, int Row) ToOffset();        // odd-r
    public static Hex FromOffset(int col, int row);
}

public sealed class Layer<T>                     // плоский массив row * Width + col
{
    public T this[Hex h] { get; set; }
    public bool Contains(Hex h);
    public ReadOnlySpan<T> Raw { get; }
}
```

## 3. Определения (JSON → record)

`sealed record` с позиционными параметрами, имена полей 1:1 с ключами
JSON (camelCase в файле, PascalCase в коде). Enum-ы — строками.

```csharp
public enum NodeType { Empty, Home, Workstation, Server, Router, IoT, Controller }
public enum Os { Kestrel, Bastion, Mote, Forge }
public enum LinkKind { Backbone, Vpn, Sneakernet }
public enum Rarity { Common, ZeroDay }
public enum TriggerKind { EveryTurn, Schedule, Chance, ExposureLimit }
public enum EffectKind { PatchWave, Audit, Burnout, LinkOutage, Migration }

// rules.json
public sealed record RulesDef(
    int ActionPoints, int SlotLimit, int PatchMax,
    int MoveCost, int ExploitCost, int ScanCost, int LurkCost,
    float NoiseDecay, float NoiseAudit, float ScanNoise,
    int StartCompute, string[] StartingExploits,
    int DawnTurn, int ScorePerTurnLeft, int ScorePerNode,
    int MutatorBudget);

// nodes.json
public sealed record NodeTypeDef(
    NodeType Type, int BasePatch, float Yield, int ScanRadius, Dictionary<Os, float> OsWeights);

// exploits.json
public sealed record ExploitDef(
    string Id, string Name, Os TargetOs, int Power, int MaxCharges,
    int ComputeCost, int ExposureLimit, float NoisePerUse, Rarity Rarity);

// modifiers.json
public sealed record Modifier(
    string Id, string Name, string Description, string[] Tags, int MutatorCost,
    Dictionary<Os, int>? PowerBonus = null, float NoiseMult = 1f, float ComputeMult = 1f,
    int ExtraActionPoints = 0, int ExtraCharges = 0, int ExtraSlotLimit = 0,
    int ScanRadiusBonus = 0, string? StartingExploit = null);

// events.json
public sealed record SchedulePhase(int FromTurn, int Every);
public sealed record EventTrigger(TriggerKind Kind, SchedulePhase[]? Schedule = null, int FromTurn = 1, float Chance = 0f);
public sealed record EventEffect(EffectKind Kind, int Amount = 0, int Duration = 0, int LimitMultiplier = 1);
public sealed record WorldEventDef(string Id, string Name, string Text, EventTrigger Trigger, EventEffect Effect);

// mapgen.json
public sealed record LinkCounts(int Backbone, int Vpn, int Sneakernet);
public sealed record MapSizeDef(
    int Width, int Height, int Clusters, int ExtraCorridors, int Datacenters, int Factories,
    LinkCounts Links, int MinTargetDist);
public sealed record ClusterProfile(
    string Id, Dictionary<NodeType, float> NodeWeights, NodeType? Center, int PatchBonus, string[] NamePool);
public sealed record MapGenDef(
    int ClusterRadiusMin, int ClusterRadiusMax, float HoleChance, int MinCenterDist,
    MapSizeDef Size, ClusterProfile[] Profiles);
```

Что ушло по сравнению с v2: `VictoryDef`, `AiProfileDef`,
`FactionPresetDef`, поля `MaxFactions`, `SpawnCost`, `Harden*`,
`NoiseVisible`, `LateStartBonus`, `RequireAllRouters`, `SharedVision`,
`AlliesAdjacent`, `AiValue`, `ComputeCost`/`Requires` у модификаторов,
таблицы «по числу фракций» (`Sizes[]` → одна `Size`).

```csharp
public sealed class Rules
{
    public RulesDef Core { get; }
    public IReadOnlyDictionary<NodeType, NodeTypeDef> NodeTypes { get; }
    public IReadOnlyDictionary<string, ExploitDef> Exploits { get; }
    public IReadOnlyDictionary<string, Modifier> Modifiers { get; }
    public IReadOnlyList<WorldEventDef> Events { get; }
    public MapGenDef MapGen { get; }
    public IReadOnlyDictionary<string, ClusterProfile> Profiles { get; }

    public static Rules Load(string dir);   // валидирует, бросает InvalidDataException с файлом и Id
}
```

## 4. Состояние забега

```csharp
public sealed class World                    // есть, без изменений
{
    public Layer<NodeType> Type;  public Layer<Os> Os;
    public Layer<int> Patch;      public Layer<int> Hardening;   // Hardening не используется в v3, всегда 0
    public Layer<int> Owner;      // -1 нейтрал, 0 игрок
    public Layer<float> Noise;
    public Layer<bool>[] KnownBy; // в v3 длина 1
    public Layer<string> Name;
    public List<Link> Links;      // Link(Hex A, Hex B, LinkKind Kind) { int DisabledUntilTurn }
}

public sealed class ExploitSlot { public string DefId; public int Charges; }

public sealed class Daemon
{
    public Hex Pos; public int ActionPoints;
    public List<ExploitSlot> Exploits;
}

public sealed record RunStats(            // свёртка мутаторов, считается в NewRun
    IReadOnlyDictionary<Os, int> PowerBonus, float NoiseMult, float ComputeMult,
    int ActionPoints, int SlotLimit, int ExtraCharges, int ScanRadiusBonus);

public enum Outcome { Running, Won, LostAudit, LostDawn }

public sealed class RunState
{
    public int Seed; public int Turn;
    public World World; public Hex Target;                 // сервер датацентра
    public Daemon Daemon; public int Compute;
    public List<string> Mutators; public RunStats Stats;
    public Dictionary<string, int> Exposure;               // DefId → применений с последнего выгорания
    public Dictionary<string, int> ExposureLimit;          // DefId → текущий порог
    public Dictionary<string, int> PowerPenalty;           // DefId → накопленное −Power
    public ulong RngState;                                  // состояние Rng — часть сейва и реплея
    public Outcome Outcome; public int Score;
    public RunState Clone();
}
```

`Hardening` остаётся в `World`, чтобы не трогать генератор и тесты v2;
правила v3 его не читают. `KnownBy` остаётся массивом по той же причине.

## 5. Действия и события

```csharp
public abstract record Action;
public sealed record Move(Hex To) : Action;
public sealed record Exploit(int SlotIx, Hex Target) : Action;
public sealed record Scan : Action;
public sealed record Lurk : Action;
public sealed record Compile(string ExploitId, int SlotIx) : Action;   // SlotIx — куда положить (пустой или заменить)
public sealed record EndTurn : Action;

public abstract record SimEvent(int Turn);
public sealed record NodeCaptured(int Turn, Hex Node) : SimEvent(Turn);
public sealed record CaptureFailed(int Turn, Hex Node, string Reason) : SimEvent(Turn);
public sealed record ExploitCompiled(int Turn, string DefId) : SimEvent(Turn);
public sealed record ExploitBurned(int Turn, string DefId, int NewPower) : SimEvent(Turn);
public sealed record NodeAudited(int Turn, Hex Node) : SimEvent(Turn);
public sealed record WorldEventFired(int Turn, string Id, string Text) : SimEvent(Turn);
public sealed record TurnStarted(int Turn, int Income) : SimEvent(Turn);
public sealed record RunEnded(int Turn, Outcome Outcome, int Score) : SimEvent(Turn);

public sealed record Verdict(bool Ok, string Reason);            // Verdict.Yes, Verdict.No("…")
public sealed record CaptureCheck(bool Possible, int Need, int Have, bool OsMatch, bool Known, string Reason);
```

## 6. API симуляции

```csharp
public static class Sim
{
    public static RunState NewRun(Rules rules, int seed, IReadOnlyList<string> mutators);
    public static RunState NewRun(Rules rules, MapResult map, int seed, IReadOnlyList<string> mutators); // для тестов на ручной карте
    public static Verdict Check(Rules rules, RunState s, Action a);
    public static IReadOnlyList<SimEvent> Apply(Rules rules, RunState s, Action a);   // бросает, если Check не Ok
    public static IReadOnlyList<Action> Legal(Rules rules, RunState s);
    public static CaptureCheck CanCapture(Rules rules, RunState s, int slotIx, Hex target);
}
```

`Rng` живёт внутри `Apply`: создаётся из `RngState`, состояние
записывается обратно. Так сейв и реплей (сид + список действий)
воспроизводят забег бит в бит.

## 7. Файлы данных

| Файл | Содержимое |
|---|---|
| `data/rules.json` | `RulesDef` |
| `data/nodes.json` | `NodeTypeDef[]` — 6 типов |
| `data/exploits.json` | `ExploitDef[]` — 18 common + зеро-деи |
| `data/modifiers.json` | `Modifier[]` — мутаторы |
| `data/events.json` | `WorldEventDef[]` — 5 событий |
| `data/mapgen.json` | `MapGenDef` — один размер, профили кластеров с пулами имён |

`ai.json` и `factions.json` удаляются на первом уикенде вместе
с их записями.

## 8. Инварианты (тесты на данных)

- Все `Id` уникальны; `StartingExploit`, `StartingExploits` ссылаются
  на существующие эксплойты.
- Для каждой ОС и тира есть ровно один `Common` эксплойт, кроме `Forge`
  T1–T2.
- Все `NodeType`, кроме `Empty`, определены; `NodeWeights` и `OsWeights`
  суммируются в 1; у профиля с `Center` он не `Empty`.
- Сумма `MutatorCost` любого набора в тестах ≤ `MutatorBudget`.
- `Hex` round-trip на всей карте; `DistanceTo` симметрична.
- `Clone()` и JSON round-trip дают равное состояние.
- Забег на фиксированном сиде и списке действий воспроизводится
  бит в бит.

## 9. Расширение без правок кода

| Хочу | Делаю |
|---|---|
| Новый тип узла | Строка в `nodes.json` + глиф + профиль генератора |
| Новый мутатор с новым эффектом | Числовое поле в `Modifier` + чтение в правиле — один код-патч, дальше данные |
| Новое событие того же вида | Строка в `events.json` |
| Событие нового вида | Новый `EffectKind` + его `Apply` — код-патч |
| Второй профиль карты | Второй `MapGenDef` в данных + выбор на старте |
| Ежедневный забег | Сид = дата, ничего в `Sim` |
