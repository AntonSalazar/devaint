using System.Collections.Generic;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Действия на своём сервере: Compile (<c>01-RULES.md §5</c>).
/// </summary>
public static partial class Sim
{
    /// <summary>
    /// Метод проверки Compile: daemon на своём сервере, эксплойт компилируемый, Compute хватает,
    /// слот либо существующий (замена), либо следующий свободный (в пределах лимита).
    /// Очки действия не тратятся - компиляция бесплатна по времени, платна по Compute.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="compile">Действие.</param>
    /// <returns>Вернет вердикт.</returns>
    public static Verdict CheckCompile(Rules rules, RunState state, Compile compile)
    {
        World world = state.World;
        Daemon daemon = state.Daemon;

        if (world.Type[daemon.Pos] != NodeType.Server)
        {
            return Verdict.No("daemon is not on a server");
        }
        if (world.Owner[daemon.Pos] != Player)
        {
            return Verdict.No("server is not yours");
        }
        if (!rules.Exploits.TryGetValue(compile.ExploitId, out ExploitDef? def))
        {
            return Verdict.No($"unknown exploit '{compile.ExploitId}'");
        }
        if (def.Rarity != Rarity.Common)
        {
            return Verdict.No("zero-days cannot be compiled");
        }
        if (state.Compute < def.ComputeCost)
        {
            return Verdict.No($"need {def.ComputeCost} compute, have {state.Compute}");
        }
        if (compile.SlotIx < 0 || compile.SlotIx > daemon.Exploits.Count)
        {
            return Verdict.No("no such slot");
        }
        if (compile.SlotIx == daemon.Exploits.Count && daemon.Exploits.Count >= state.Stats.SlotLimit)
        {
            return Verdict.No("no free slot");
        }

        return Verdict.Yes;
    }


    /// <summary>
    /// Метод применения Compile: списать Compute, положить эксплойт с полными зарядами в слот.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="compile">Действие.</param>
    /// <param name="events">Куда складывать события.</param>
    public static void ApplyCompile(Rules rules, RunState state, Compile compile, List<SimEvent> events)
    {
        Daemon daemon = state.Daemon;
        ExploitDef def = rules.Exploits[compile.ExploitId];
        ExploitSlot slot = new(def.Id, MaxCharges(rules, state.Stats, def.Id));

        state.Compute -= def.ComputeCost;
        if (compile.SlotIx == daemon.Exploits.Count)
        {
            daemon.Exploits.Add(slot);
        }
        else
        {
            daemon.Exploits[compile.SlotIx] = slot;
        }

        events.Add(new ExploitCompiled(state.Turn, def.Id));
    }
}
