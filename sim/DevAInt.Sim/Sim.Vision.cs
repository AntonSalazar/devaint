using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Туман: что игрок видит и когда это раскрывается.
/// </summary>
public static partial class Sim
{
    /// <summary>
    /// Метод снятия тумана вокруг гекса радиусом скана этого узла.
    /// Радиус берётся из типа узла (роутер видит дальше) плюс бонус мутаторов.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="state">Состояние забега.</param>
    /// <param name="center">Откуда смотрим.</param>
    public static void Reveal(Rules rules, RunState state, Hex center)
    {
        NodeType type = state.World.Type[center];
        int radius = rules.NodeTypes[type].ScanRadius + state.Stats.ScanRadiusBonus;
        RevealRadius(state, center, radius);
    }


    /// <summary>
    /// Метод снятия тумана со всех гексов не дальше <paramref name="radius"/> от центра.
    /// Дистанция - по прямой в гексах, стены зазора не мешают: это "аномалия трафика", а не луч света.
    /// </summary>
    /// <param name="state">Состояние забега.</param>
    /// <param name="center">Центр.</param>
    /// <param name="radius">Радиус в гексах.</param>
    public static void RevealRadius(RunState state, Hex center, int radius)
    {
        Layer<bool> known = state.World.KnownBy[Player];
        foreach (Hex hex in state.World.Cells())
        {
            if (center.DistanceTo(hex) <= radius)
            {
                known[hex] = true;
            }
        }
    }


    /// <summary>
    /// Метод снятия тумана с гекса и его соседей (включая концы линков) - как после захвата.
    /// </summary>
    /// <param name="state">Состояние забега.</param>
    /// <param name="hex">Гекс.</param>
    public static void RevealAround(RunState state, Hex hex)
    {
        Layer<bool> known = state.World.KnownBy[Player];
        known[hex] = true;
        foreach (Hex neighbor in state.World.Neighbors(hex))
        {
            known[neighbor] = true;
        }
    }
}
