using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Перебор всех легальных действий. Нужен подсветке в UI, авто-забегам и дымовым тестам.
/// </summary>
public static partial class Sim
{
    /// <summary>
    /// Метод перебора: собрать всех кандидатов и оставить тех, кого пропускает <see cref="Check"/>.
    /// Порядок фиксированный, чтобы авто-забег от сида был воспроизводим.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <returns>Вернет список легальных действий.</returns>
    public static IReadOnlyList<Action> Legal(Rules rules, RunState state)
    {
        if (state.Outcome != Outcome.Running)
        {
            return [];
        }

        List<Action> candidates = [];
        List<Hex> neighbors = [.. Neighbors(state, state.Daemon.Pos)];

        foreach (Hex hex in neighbors)
        {
            candidates.Add(new Move(hex));
        }
        for (int slot = 0; slot < state.Daemon.Exploits.Count; slot++)
        {
            foreach (Hex hex in neighbors)
            {
                candidates.Add(new Exploit(slot, hex));
            }
        }
        candidates.Add(new Scan());
        candidates.Add(new Lurk());
        foreach (string id in rules.Exploits.Keys.OrderBy(static k => k, System.StringComparer.Ordinal))
        {
            for (int slot = 0; slot <= state.Daemon.Exploits.Count; slot++)
            {
                candidates.Add(new Compile(id, slot));
            }
        }
        candidates.Add(new EndTurn());

        return [.. candidates.Where(a => Check(rules, state, a).Ok)];
    }
}
