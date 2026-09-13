using System;
using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Правила забега. Статический класс без собственных данных: принимает состояние
/// и действие, меняет состояние, возвращает события.
/// Класс partial - разложен по файлам: <c>Sim.cs</c> (создание забега и общий вход),
/// <c>Sim.Vision.cs</c> (туман), дальше - действия, ход, мировая фаза.
/// </summary>
public static partial class Sim
{
    /// <summary>
    /// Индекс единственной фракции игрока в <see cref="World.Owner"/> и <see cref="World.KnownBy"/>.
    /// Нейтральный узел - <see cref="Neutral"/>.
    /// </summary>
    public const int Player = 0;

    /// <summary>
    /// Владелец нейтрального узла.
    /// </summary>
    public const int Neutral = -1;


    /// <summary>
    /// Метод создания нового забега от сида: генерирует карту и ставит daemon'а на старт.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="seed">Сид.</param>
    /// <param name="mutators">Id мутаторов, выбранных на старте.</param>
    /// <returns>Вернет состояние на начало первого хода.</returns>
    public static RunState NewRun(Rules rules, int seed, IReadOnlyList<string> mutators)
    {
        ValidateMutators(rules, mutators);
        MapResult map = MapGen.Generate(rules, seed);
        return NewRun(rules, map, seed, mutators);
    }


    /// <summary>
    /// Метод создания нового забега на готовой карте.
    /// Отдельный вход нужен тестам: они строят крошечную карту руками и не зависят от генератора.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="map">Готовая карта со стартом и целью.</param>
    /// <param name="seed">Сид (запоминается в состоянии; для случайностей забега берётся сид карты).</param>
    /// <param name="mutators">Id мутаторов, выбранных на старте.</param>
    /// <returns>Вернет состояние на начало первого хода.</returns>
    public static RunState NewRun(Rules rules, MapResult map, int seed, IReadOnlyList<string> mutators)
    {
        ValidateMutators(rules, mutators);
        World world = map.World;
        RunStats stats = RunStats.From(rules, mutators);

        // 1. Daemon на старте с полными очками действия. Стартовый узел - свой.
        Daemon daemon = new(map.Start, stats.ActionPoints);
        world.Owner[map.Start] = Player;

        // 2. Стартовые эксплойты: общие из rules.json плюс те, что дают мутаторы (zero_day_cache).
        //    Каждый слот - с полными зарядами, не больше лимита слотов.
        IEnumerable<string> startingIds = rules.Core.StartingExploits
            .Concat(mutators.Select(id => rules.Modifiers[id].StartingExploit).OfType<string>());
        foreach (string defId in startingIds.Take(stats.SlotLimit))
        {
            daemon.Exploits.Add(new ExploitSlot(defId, MaxCharges(rules, stats, defId)));
        }

        // 3. Состояние.
        RunState state = new(seed, world, map.Target, daemon, [.. mutators], stats)
        {
            Compute = rules.Core.StartCompute,
            RngState = new Rng(map.SeedUsed).State,
        };

        // 4. Счётчики выгорания - по всем эксплойтам сразу, чтобы правила не проверяли "есть ли ключ".
        foreach (ExploitDef def in rules.Exploits.Values)
        {
            state.Exposure[def.Id] = 0;
            state.ExposureLimit[def.Id] = def.ExposureLimit;
            state.PowerPenalty[def.Id] = 0;
        }

        // 5. Туман: видно вокруг старта как после бесплатного скана.
        Reveal(rules, state, map.Start);
        return state;
    }


    /// <summary>
    /// Метод подсчёта максимума зарядов слота: база эксплойта плюс бонус мутаторов.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="stats">Свёртка мутаторов.</param>
    /// <param name="defId">Id эксплойта.</param>
    /// <returns>Вернет максимум зарядов.</returns>
    public static int MaxCharges(Rules rules, RunStats stats, string defId) =>
        rules.Exploits[defId].MaxCharges + stats.ExtraCharges;


    /// <summary>
    /// Метод завершения забега: записать исход и счёт, добавить событие.
    /// После этого <see cref="Check"/> отклоняет любые действия.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="outcome">Чем закончился.</param>
    /// <param name="events">Куда складывать события.</param>
    public static void FinishRun(Rules rules, RunState state, Outcome outcome, List<SimEvent> events)
    {
        state.Outcome = outcome;
        state.Score = Score(rules, state, outcome);
        events.Add(new RunEnded(state.Turn, outcome, state.Score));
    }


    /// <summary>
    /// Метод подсчёта счёта (<c>01-RULES.md §9</c>): при победе - за каждый ход до рассвета
    /// и за каждый свой узел; при поражении - только за узлы.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="outcome">Исход.</param>
    /// <returns>Вернет счёт.</returns>
    public static int Score(Rules rules, RunState state, Outcome outcome)
    {
        int owned = state.World.Nodes().Count(h => state.World.Owner[h] == Player);
        int score = owned * rules.Core.ScorePerNode;
        if (outcome == Outcome.Won)
        {
            score += (rules.Core.DawnTurn - state.Turn) * rules.Core.ScorePerTurnLeft;
        }
        return score;
    }


    /// <summary>
    /// Метод проверки набора мутаторов: все существуют, без повторов, в бюджете.
    /// Бросает <see cref="ArgumentException"/> с понятным текстом - это ошибка вызывающего, не правило игры.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="mutators">Id мутаторов.</param>
    private static void ValidateMutators(Rules rules, IReadOnlyList<string> mutators)
    {
        int cost = 0;
        HashSet<string> seen = [];
        foreach (string id in mutators)
        {
            if (!rules.Modifiers.TryGetValue(id, out Modifier? mod))
            {
                throw new ArgumentException($"unknown mutator '{id}'", nameof(mutators));
            }
            if (!seen.Add(id))
            {
                throw new ArgumentException($"mutator '{id}' picked twice", nameof(mutators));
            }
            cost += mod.MutatorCost;
        }

        if (cost > rules.Core.MutatorBudget)
        {
            throw new ArgumentException($"mutators cost {cost}, budget is {rules.Core.MutatorBudget}", nameof(mutators));
        }
    }
}
