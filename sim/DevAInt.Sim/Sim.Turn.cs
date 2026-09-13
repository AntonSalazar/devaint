using System;
using System.Collections.Generic;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Конец хода и начало следующего (<c>01-RULES.md §8</c>):
/// мировая фаза → рассвет? → ход + 1 → upkeep.
/// </summary>
public static partial class Sim
{
    /// <summary>
    /// Метод применения EndTurn. Проверок нет: закончить ход можно всегда, пока забег идёт.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="events">Куда складывать события.</param>
    public static void ApplyEndTurn(Rules rules, RunState state, List<SimEvent> events)
    {
        // 1. Люди действуют: аудит, патчи, выгорание, линки. Тут daemon может погибнуть.
        WorldPhase(rules, state, events);
        if (state.Outcome != Outcome.Running)
        {
            return;
        }

        // 2. Следующий ход. Если это ход рассвета - ночь кончилась.
        state.Turn += 1;
        if (state.Turn >= rules.Core.DawnTurn)
        {
            FinishRun(rules, state, Outcome.LostDawn, events);
            return;
        }

        // 3. Upkeep нового хода.
        Upkeep(rules, state, events);
    }


    /// <summary>
    /// Метод начала хода: шум затухает, приходит доход, заряды пополняются на своём сервере,
    /// очки действия сбрасываются до максимума.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="events">Куда складывать события.</param>
    public static void Upkeep(Rules rules, RunState state, List<SimEvent> events)
    {
        World world = state.World;
        Daemon daemon = state.Daemon;

        // Шум затухает везде.
        foreach (Hex hex in world.Nodes())
        {
            world.Noise[hex] *= rules.Core.NoiseDecay;
        }

        // Доход: сумма Yield по своим узлам × множитель, вниз до целого.
        int income = Income(rules, state);
        state.Compute += income;

        // Заряды: только если daemon стоит на своём сервере.
        if (world.Type[daemon.Pos] == NodeType.Server && world.Owner[daemon.Pos] == Player)
        {
            foreach (ExploitSlot slot in daemon.Exploits)
            {
                slot.Charges = Math.Min(slot.Charges + 1, MaxCharges(rules, state.Stats, slot.DefId));
            }
        }

        daemon.ActionPoints = state.Stats.ActionPoints;
        events.Add(new TurnStarted(state.Turn, income));
    }


    /// <summary>
    /// Метод подсчёта дохода за ход. Обход узлов в фиксированном порядке (по рядам),
    /// чтобы сумма float не зависела от порядка - это условие детерминизма.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <returns>Вернет доход в Compute.</returns>
    public static int Income(Rules rules, RunState state)
    {
        World world = state.World;
        float total = 0f;
        foreach (Hex hex in world.Nodes())
        {
            if (world.Owner[hex] == Player)
            {
                total += rules.NodeTypes[world.Type[hex]].Yield;
            }
        }

        return (int)(total * state.Stats.ComputeMult);
    }
}
