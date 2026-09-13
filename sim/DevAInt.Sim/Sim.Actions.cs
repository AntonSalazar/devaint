using System;
using System.Collections.Generic;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Общий вход для действий: проверить и применить.
/// Здесь только разбор "какое действие пришло"; сами правила - в файлах по темам
/// (<c>Sim.Daemon.cs</c> - действия daemon'а, дальше - сервер и ход).
/// </summary>
public static partial class Sim
{
    /// <summary>
    /// Метод проверки, можно ли применить действие. Ничего не меняет.
    /// UI зовёт его для подсветки, <see cref="Apply"/> - перед применением.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="action">Действие.</param>
    /// <returns>Вернет вердикт: можно, или нельзя и почему.</returns>
    public static Verdict Check(Rules rules, RunState state, Action action)
    {
        // Забег окончен - никаких действий.
        if (state.Outcome != Outcome.Running)
        {
            return Verdict.No("run is over");
        }

        // `switch` по типу: каждая ветка ловит свой record и отдаёт его правилу.
        return action switch
        {
            Move move => CheckMove(rules, state, move),
            Scan => CheckScan(rules, state),
            Lurk => CheckLurk(rules, state),
            Exploit => Verdict.No("not implemented yet"),
            Compile => Verdict.No("not implemented yet"),
            EndTurn => Verdict.No("not implemented yet"),
            _ => Verdict.No($"unknown action {action}"),
        };
    }


    /// <summary>
    /// Метод применения действия. Меняет состояние на месте и возвращает события.
    /// Нелегальное действие - исключение: это ошибка вызывающего, он обязан был спросить <see cref="Check"/>.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="action">Действие.</param>
    /// <returns>Вернет список событий, произошедших при применении.</returns>
    public static IReadOnlyList<SimEvent> Apply(Rules rules, RunState state, Action action)
    {
        Verdict verdict = Check(rules, state, action);
        if (!verdict.Ok)
        {
            throw new InvalidOperationException($"illegal action {action}: {verdict.Reason}");
        }

        // Move, Scan и Lurk событий не рождают; список пригодится Exploit, Compile и EndTurn.
        List<SimEvent> events = [];
        switch (action)
        {
            case Move move:
                ApplyMove(rules, state, move);
                break;
            case Scan:
                ApplyScan(rules, state);
                break;
            case Lurk:
                ApplyLurk(rules, state);
                break;
            default:
                throw new InvalidOperationException($"unknown action {action}");
        }

        return events;
    }
}
