using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Чем закончился забег.
/// </summary>
public enum Outcome
{
    /// <summary>
    /// Ещё идёт.
    /// </summary>
    Running,

    /// <summary>
    /// Датацентр захвачен.
    /// </summary>
    Won,

    /// <summary>
    /// Daemon вычищен аудитом.
    /// </summary>
    LostAudit,

    /// <summary>
    /// Наступил рассвет.
    /// </summary>
    LostDawn
}


/// <summary>
/// Слот эксплойта у daemon'а: какой эксплойт лежит и сколько у него зарядов.
/// Это класс, а не record, потому что заряды меняются на месте при каждом захвате.
/// </summary>
/// <param name="defId">Id эксплойта из <c>exploits.json</c>.</param>
/// <param name="charges">Сколько зарядов сейчас.</param>
public sealed class ExploitSlot(string defId, int charges)
{
    /// <summary>
    /// Id эксплойта. Само описание (сила, ОС, цена) живёт в <see cref="Rules.Exploits"/>,
    /// здесь только ссылка на него по имени.
    /// </summary>
    public string DefId { get; set; } = defId;

    /// <summary>
    /// Зарядов осталось. 0 - слот пустой до пополнения на сервере.
    /// </summary>
    public int Charges { get; set; } = charges;


    /// <summary>
    /// Метод создания независимой копии слота.
    /// </summary>
    /// <returns>Вернет копию.</returns>
    public ExploitSlot Clone() => new(DefId, Charges);
}


/// <summary>
/// Daemon - единственный юнит игрока: где стоит, сколько очков действия, какие эксплойты несёт.
/// </summary>
/// <param name="pos">Стартовый гекс.</param>
/// <param name="actionPoints">Очков действия на первый ход.</param>
public sealed class Daemon(Hex pos, int actionPoints)
{
    /// <summary>
    /// Где стоит. Всегда свой узел (<c>Owner == 0</c>).
    /// </summary>
    public Hex Pos { get; set; } = pos;

    /// <summary>
    /// Очков действия осталось в этом ходу. В начале хода сбрасывается до максимума.
    /// </summary>
    public int ActionPoints { get; set; } = actionPoints;

    /// <summary>
    /// Слоты эксплойтов. Длина не больше <see cref="RunStats.SlotLimit"/>.
    /// </summary>
    public List<ExploitSlot> Exploits { get; } = [];


    /// <summary>
    /// Метод создания независимой копии daemon'а.
    /// </summary>
    /// <returns>Вернет копию.</returns>
    public Daemon Clone()
    {
        Daemon copy = new(Pos, ActionPoints);
        copy.Exploits.AddRange(Exploits.Select(static slot => slot.Clone()));
        return copy;
    }
}


/// <summary>
/// Свёртка всех мутаторов забега в готовые коэффициенты.
/// Считается один раз в <see cref="Sim.NewRun(Rules, MapResult, int, IReadOnlyList{string})"/>,
/// дальше правила читают только её и не заглядывают в список мутаторов.
/// Record: после создания не меняется.
/// </summary>
/// <param name="PowerBonus">Бонус к Power эксплойтов по каждой ОС (ключи - все четыре ОС, по умолчанию 0).</param>
/// <param name="NoiseMult">Множитель шума от действий.</param>
/// <param name="ComputeMult">Множитель дохода.</param>
/// <param name="ActionPoints">Очков действия в ход.</param>
/// <param name="SlotLimit">Слотов эксплойтов.</param>
/// <param name="ExtraCharges">Бонус к максимуму зарядов каждого слота.</param>
/// <param name="ScanRadiusBonus">Бонус к радиусу скана.</param>
public sealed record RunStats(
    Dictionary<Os, int> PowerBonus,
    float NoiseMult,
    float ComputeMult,
    int ActionPoints,
    int SlotLimit,
    int ExtraCharges,
    int ScanRadiusBonus)
{
    /// <summary>
    /// Статичный метод свёртки: бонусы складываются, множители перемножаются.
    /// Список мутаторов уже проверен (<see cref="Sim.NewRun(Rules, int, IReadOnlyList{string})"/>), здесь только арифметика.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="mutators">Id мутаторов забега.</param>
    /// <returns>Вернет свёртку.</returns>
    public static RunStats From(Rules rules, IReadOnlyList<string> mutators)
    {
        // Начинаем с базовых чисел из rules.json.
        Dictionary<Os, int> powerBonus = [];
        foreach (Os os in System.Enum.GetValues<Os>())
        {
            powerBonus[os] = 0;
        }
        float noiseMult = 1f;
        float computeMult = 1f;
        int actionPoints = rules.Core.ActionPoints;
        int slotLimit = rules.Core.SlotLimit;
        int extraCharges = 0;
        int scanRadiusBonus = 0;

        // Каждый мутатор добавляет своё.
        foreach (string id in mutators)
        {
            Modifier mod = rules.Modifiers[id];
            if (mod.PowerBonus is not null)
            {
                foreach ((Os os, int bonus) in mod.PowerBonus)
                {
                    powerBonus[os] += bonus;
                }
            }
            noiseMult *= mod.NoiseMult;
            computeMult *= mod.ComputeMult;
            actionPoints += mod.ExtraActionPoints;
            slotLimit += mod.ExtraSlotLimit;
            extraCharges += mod.ExtraCharges;
            scanRadiusBonus += mod.ScanRadiusBonus;
        }

        return new RunStats(powerBonus, noiseMult, computeMult, actionPoints, slotLimit, extraCharges, scanRadiusBonus);
    }
}


/// <summary>
/// Состояние забега целиком. Единственный способ его изменить - <c>Sim.Apply</c> (появится вместе с действиями).
/// Всё здесь - простые данные без ссылок на Godot, чтобы состояние можно было
/// сохранить в JSON, скопировать и сравнить.
/// </summary>
/// <remarks>
/// Собирается в <see cref="Sim.NewRun(Rules, MapResult, int, IReadOnlyList{string})"/>, напрямую конструктор звать не нужно.
/// </remarks>
/// <param name="seed">Сид забега.</param>
/// <param name="world">Карта.</param>
/// <param name="target">Гекс цели.</param>
/// <param name="daemon">Daemon.</param>
/// <param name="mutators">Id мутаторов.</param>
/// <param name="stats">Свёртка мутаторов.</param>
public sealed class RunState(int seed, World world, Hex target, Daemon daemon, List<string> mutators, RunStats stats)
{
    /// <summary>
    /// Сид, с которого сгенерирована карта. Вместе со списком действий воспроизводит забег.
    /// </summary>
    public int Seed { get; } = seed;

    /// <summary>
    /// Номер хода, с 1.
    /// </summary>
    public int Turn { get; set; } = 1;

    /// <summary>
    /// Карта со всеми слоями.
    /// </summary>
    public World World { get; } = world;

    /// <summary>
    /// Цель забега - сервер датацентра. Захватил - победил.
    /// </summary>
    public Hex Target { get; } = target;

    /// <summary>
    /// Единственный юнит игрока.
    /// </summary>
    public Daemon Daemon { get; } = daemon;

    /// <summary>
    /// Ресурс. Капает с узлов, тратится на компиляцию.
    /// </summary>
    public int Compute { get; set; }

    /// <summary>
    /// Id мутаторов, выбранных на старте. Только для UI и сейва: правила читают <see cref="Stats"/>.
    /// </summary>
    public List<string> Mutators { get; } = mutators;

    /// <summary>
    /// Свёртка мутаторов в коэффициенты.
    /// </summary>
    public RunStats Stats { get; } = stats;

    /// <summary>
    /// Сколько раз применён каждый эксплойт с последнего выгорания. Ключ - Id эксплойта.
    /// </summary>
    public Dictionary<string, int> Exposure { get; } = [];

    /// <summary>
    /// Текущий порог выгорания каждого эксплойта. После выгорания удваивается.
    /// </summary>
    public Dictionary<string, int> ExposureLimit { get; } = [];

    /// <summary>
    /// Накопленный штраф к Power каждого эксплойта от выгораний.
    /// </summary>
    public Dictionary<string, int> PowerPenalty { get; } = [];

    /// <summary>
    /// Состояние генератора случайных чисел. Часть состояния, чтобы сейв и реплей
    /// давали те же патч-волны и отключения линков.
    /// </summary>
    public ulong RngState { get; set; }

    /// <summary>
    /// Чем закончился забег. Пока <see cref="Outcome.Running"/> - действия принимаются.
    /// </summary>
    public Outcome Outcome { get; set; } = Outcome.Running;

    /// <summary>
    /// Счёт. Считается один раз в момент окончания забега.
    /// </summary>
    public int Score { get; set; }


    /// <summary>
    /// Метод создания независимой копии состояния (снапшот для отмотки, тестов, реплея).
    /// </summary>
    /// <returns>Вернет копию.</returns>
    public RunState Clone()
    {
        RunState copy = new(Seed, World.Clone(), Target, Daemon.Clone(), [.. Mutators], Stats)
        {
            Turn = Turn,
            Compute = Compute,
            RngState = RngState,
            Outcome = Outcome,
            Score = Score,
        };
        foreach ((string id, int value) in Exposure)
        {
            copy.Exposure[id] = value;
        }
        foreach ((string id, int value) in ExposureLimit)
        {
            copy.ExposureLimit[id] = value;
        }
        foreach ((string id, int value) in PowerPenalty)
        {
            copy.PowerPenalty[id] = value;
        }
        return copy;
    }
}
