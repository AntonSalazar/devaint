using System.Collections.Generic;
using System.Linq;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Действия daemon'а: Move, Scan, Lurk (Exploit - отдельным файлом, он большой).
/// У каждого действия две функции: <c>Check*</c> отвечает "можно ли и почему нет",
/// <c>Apply*</c> меняет состояние и уверен, что можно (Check уже был).
/// </summary>
public static partial class Sim
{
    // ---------------------------------------------------------------- Move

    /// <summary>
    /// Метод проверки Move: очков хватает, цель - соседний свой узел.
    /// Порядок проверок - от дешёвой к дорогой и от общей к частной: UI покажет самую понятную причину.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="move">Действие.</param>
    /// <returns>Вернет вердикт.</returns>
    public static Verdict CheckMove(Rules rules, RunState state, Move move)
    {
        World world = state.World;
        Daemon daemon = state.Daemon;

        if (daemon.ActionPoints < rules.Core.MoveCost)
        {
            return Verdict.No("not enough action points");
        }
        if (!world.IsNode(move.To))
        {
            return Verdict.No("target is not a node");
        }
        if (move.To == daemon.Pos)
        {
            return Verdict.No("already there");
        }
        if (world.Owner[move.To] != Player)
        {
            return Verdict.No("target is not yours");
        }
        if (!Adjacent(state, daemon.Pos, move.To))
        {
            return Verdict.No("target is not adjacent");
        }

        return Verdict.Yes;
    }


    /// <summary>
    /// Метод применения Move: перешли, заплатили очки. Шума нет, событий нет,
    /// поэтому список событий сюда не передаётся - Move в него ничего не кладёт.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="move">Действие.</param>
    public static void ApplyMove(Rules rules, RunState state, Move move)
    {
        state.Daemon.Pos = move.To;
        state.Daemon.ActionPoints -= rules.Core.MoveCost;
    }


    // ---------------------------------------------------------------- Scan

    /// <summary>
    /// Метод проверки Scan.
    /// Единственное условие - очков действия хватает на <c>rules.Core.ScanCost</c>
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <returns>Вернет вердикт.</returns>
    public static Verdict CheckScan(Rules rules, RunState state)
    {
        Daemon daemon = state.Daemon;
        if (daemon.ActionPoints < rules.Core.ScanCost)
        {
            return Verdict.No("not enough action points");
        }
        return Verdict.Yes;
    }


    /// <summary>
    /// Метод применения Scan.
    /// Событий у скана нет.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    public static void ApplyScan(Rules rules, RunState state)
    {
        // Убираем туман.
        Daemon daemon = state.Daemon;
        Hex pos = daemon.Pos;
        Reveal(rules, state, pos);

        // Добавляем шум под узел.
        float noise = rules.Core.ScanNoise * state.Stats.NoiseMult;
        state.World.Noise[pos] += noise;

        // Списываем очки.
        daemon.ActionPoints -= rules.Core.ScanCost;
    }


    // ---------------------------------------------------------------- Lurk

    /// <summary>
    /// Метод проверки Lurk.
    /// Едиственное условие - очков хватает на <c>rules.Core.LurkCost</c>.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <returns>Вернет вердикт.</returns>
    public static Verdict CheckLurk(Rules rules, RunState state)
    {
        Daemon daemon = state.Daemon;
        if (daemon.ActionPoints < rules.Core.LurkCost)
        {
            return Verdict.No("not enough action points");
        }
        return Verdict.Yes;
    }


    /// <summary>
    /// Метод применения Lurk.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    public static void ApplyLurk(Rules rules, RunState state)
    {
        // Убираем шум.
        Daemon daemon = state.Daemon;
        Hex pos = daemon.Pos;
        state.World.Noise[pos] = 0.0f;

        // Списываем очки.
        daemon.ActionPoints -= rules.Core.LurkCost;
    }


    // ---------------------------------------------------------------- Helpers

    /// <summary>
    /// Метод проверки соседства: шесть соседей по сетке плюс концы включённых линков.
    /// Отключённый линк (<see cref="Link.DisabledUntilTurn"/> больше текущего хода) соседом не считается.
    /// </summary>
    /// <param name="state">Состояние забега.</param>
    /// <param name="a">Первый гекс.</param>
    /// <param name="b">Второй гекс.</param>
    /// <returns>Вернет флаг соседства.</returns>
    public static bool Adjacent(RunState state, Hex a, Hex b) => Neighbors(state, a).Contains(b);


    /// <summary>
    /// Метод возврата соседей с учётом отключённых линков.
    /// <see cref="World.Neighbors"/> про ходы не знает, поэтому фильтруем здесь.
    /// </summary>
    /// <param name="state">Состояние забега.</param>
    /// <param name="hex">Гекс.</param>
    /// <returns>Вернет соседей.</returns>
    public static IEnumerable<Hex> Neighbors(RunState state, Hex hex)
    {
        World world = state.World;
        for (int dir = 0; dir < Hex.Directions.Length; dir++)
        {
            Hex next = hex.Neighbor(dir);
            if (world.Type.Contains(next))
            {
                yield return next;
            }
        }

        foreach (Link link in world.Links)
        {
            if (link.DisabledUntilTurn > state.Turn)
            {
                continue;
            }
            if (link.A == hex)
            {
                yield return link.B;
            }
            else if (link.B == hex)
            {
                yield return link.A;
            }
        }
    }
}
