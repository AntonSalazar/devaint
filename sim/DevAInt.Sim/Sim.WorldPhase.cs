using System;
using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Мировая фаза - "люди как погода" (<c>01-RULES.md §8</c>). События идут в порядке
/// <c>events.json</c>; каждое - условие (когда) плюс эффект (что). Случайности берутся
/// из <see cref="RunState.RngState"/> и записываются обратно, поэтому забег воспроизводим.
/// </summary>
public static partial class Sim
{
    /// <summary>
    /// Метод мировой фазы: прогнать все события из данных.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="events">Куда складывать события.</param>
    public static void WorldPhase(Rules rules, RunState state, List<SimEvent> events)
    {
        Rng rng = Rng.FromState(state.RngState);
        foreach (WorldEventDef def in rules.Events)
        {
            if (Triggers(def.Trigger, state, rng))
            {
                ApplyEffect(rules, state, rng, def, events);
            }
        }
        state.RngState = rng.State;

        // Аудит мог снести узел под daemon'ом.
        if (state.World.Owner[state.Daemon.Pos] != Player)
        {
            FinishRun(rules, state, Outcome.LostAudit, events);
        }
    }


    /// <summary>
    /// Метод проверки условия события на этом ходу.
    /// </summary>
    /// <param name="trigger">Условие.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="rng">Генератор случайностей.</param>
    /// <returns>Вернет флаг, срабатывает ли событие.</returns>
    public static bool Triggers(EventTrigger trigger, RunState state, Rng rng)
    {
        int turn = state.Turn;
        switch (trigger.Kind)
        {
            case TriggerKind.EveryTurn:
            case TriggerKind.ExposureLimit:
                // Само условие "порог достигнут" проверяется по каждому эксплойту в эффекте.
                return true;

            case TriggerKind.Schedule:
                // Берём последнюю фазу, которая уже началась, и считаем от её первого хода:
                // fromTurn 1 every 8 → ходы 8, 16; fromTurn 17 every 5 → 21, 26 …
                SchedulePhase? phase = trigger.Schedule?
                    .Where(p => p.FromTurn <= turn)
                    .OrderBy(p => p.FromTurn)
                    .LastOrDefault();
                return phase is not null && (turn - phase.FromTurn + 1) % phase.Every == 0;

            case TriggerKind.Chance:
                return turn >= trigger.FromTurn && rng.Chance(trigger.Chance);

            default:
                return false;
        }
    }


    /// <summary>
    /// Метод применения эффекта события.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="rng">Генератор случайностей.</param>
    /// <param name="def">Событие.</param>
    /// <param name="events">Куда складывать события.</param>
    public static void ApplyEffect(Rules rules, RunState state, Rng rng, WorldEventDef def, List<SimEvent> events)
    {
        switch (def.Effect.Kind)
        {
            case EffectKind.Burnout:
                Burnout(rules, state, def, events);
                break;
            case EffectKind.Audit:
                Audit(rules, state, def, events);
                break;
            case EffectKind.PatchWave:
                PatchWave(rules, state, rng, def, events);
                break;
            case EffectKind.LinkOutage:
                LinkOutage(state, rng, def, events);
                break;
            case EffectKind.Migration:
                Migration(state, rng, def, events);
                break;
            default:
                throw new InvalidOperationException($"unknown effect {def.Effect.Kind}");
        }
    }


    /// <summary>
    /// Выгорание: каждый эксплойт, применённый не меньше порога раз, теряет Power навсегда,
    /// счётчик обнуляется, порог умножается. Обход по Id в алфавитном порядке - детерминизм.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="def">Событие.</param>
    /// <param name="events">Куда складывать события.</param>
    private static void Burnout(Rules rules, RunState state, WorldEventDef def, List<SimEvent> events)
    {
        foreach (string id in state.Exposure.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToList())
        {
            if (state.Exposure[id] < state.ExposureLimit[id])
            {
                continue;
            }

            state.PowerPenalty[id] += def.Effect.Amount;
            state.Exposure[id] = 0;
            state.ExposureLimit[id] *= def.Effect.LimitMultiplier;
            int newPower = rules.Exploits[id].Power - state.PowerPenalty[id];
            events.Add(new ExploitBurned(state.Turn, id, newPower));
        }
    }


    /// <summary>
    /// Аудит: все узлы с шумом не ниже порога становятся нейтральными, получают +Patch, шум обнуляется.
    /// Смерть daemon'а проверяется после всех событий в <see cref="WorldPhase"/>.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="def">Событие.</param>
    /// <param name="events">Куда складывать события.</param>
    private static void Audit(Rules rules, RunState state, WorldEventDef def, List<SimEvent> events)
    {
        World world = state.World;
        foreach (Hex hex in world.Nodes())
        {
            if (world.Noise[hex] < rules.Core.NoiseAudit)
            {
                continue;
            }

            world.Owner[hex] = Neutral;
            world.Patch[hex] = Math.Min(world.Patch[hex] + def.Effect.Amount, rules.Core.PatchMax);
            world.Noise[hex] = 0f;
            events.Add(new NodeAudited(state.Turn, hex));
        }
    }


    /// <summary>
    /// Патч-волна: случайная ОС, всем её узлам +Patch до капа.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="rng">Генератор случайностей.</param>
    /// <param name="def">Событие.</param>
    /// <param name="events">Куда складывать события.</param>
    private static void PatchWave(Rules rules, RunState state, Rng rng, WorldEventDef def, List<SimEvent> events)
    {
        World world = state.World;
        Os os = rng.Pick(Enum.GetValues<Os>());
        foreach (Hex hex in world.Nodes())
        {
            if (world.Os[hex] == os)
            {
                world.Patch[hex] = Math.Min(world.Patch[hex] + def.Effect.Amount, rules.Core.PatchMax);
            }
        }

        events.Add(new WorldEventFired(state.Turn, def.Id, def.Text.Replace("{os}", os.ToString())));
    }


    /// <summary>
    /// Отключение линка: случайный включённый линк не работает следующие Duration ходов.
    /// </summary>
    /// <param name="state">Состояние забега.</param>
    /// <param name="rng">Генератор случайностей.</param>
    /// <param name="def">Событие.</param>
    /// <param name="events">Куда складывать события.</param>
    private static void LinkOutage(RunState state, Rng rng, WorldEventDef def, List<SimEvent> events)
    {
        List<Link> active = [.. state.World.Links.Where(l => l.DisabledUntilTurn <= state.Turn)];
        if (active.Count == 0)
        {
            return;
        }

        Link link = rng.Pick(active);
        link.DisabledUntilTurn = state.Turn + def.Effect.Duration + 1;
        string text = def.Text
            .Replace("{link}", $"{link.Kind} {link.A.ToOffset()}-{link.B.ToOffset()}")
            .Replace("{duration}", def.Effect.Duration.ToString());
        events.Add(new WorldEventFired(state.Turn, def.Id, text));
    }


    /// <summary>
    /// Миграция: случайный нейтральный сервер переезжает на другую ОС.
    /// </summary>
    /// <param name="state">Состояние забега.</param>
    /// <param name="rng">Генератор случайностей.</param>
    /// <param name="def">Событие.</param>
    /// <param name="events">Куда складывать события.</param>
    private static void Migration(RunState state, Rng rng, WorldEventDef def, List<SimEvent> events)
    {
        World world = state.World;
        List<Hex> servers = [.. world.Nodes().Where(h => world.Type[h] == NodeType.Server && world.Owner[h] == Neutral)];
        if (servers.Count == 0)
        {
            return;
        }

        Hex server = rng.Pick(servers);
        Os[] others = [.. Enum.GetValues<Os>().Where(os => os != world.Os[server])];
        world.Os[server] = rng.Pick(others);
        events.Add(new WorldEventFired(state.Turn, def.Id, def.Text.Replace("{node}", world.Name[server])));
    }
}
