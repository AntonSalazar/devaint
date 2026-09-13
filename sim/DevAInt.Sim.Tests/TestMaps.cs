using DevAInt.Sim.Data;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Крошечные карты, построенные руками, для тестов правил.
/// Не зависят от генератора: каждый узел стоит там, где нужен тесту.
/// </summary>
public static class TestMaps
{
    /// <summary>Старт: Home, Kestrel, patch 1, угол (0,0).</summary>
    public static readonly Hex Start = Hex.FromOffset(0, 0);

    /// <summary>IoT рядом со стартом: Mote, patch 0.</summary>
    public static readonly Hex Iot = Hex.FromOffset(1, 0);

    /// <summary>Офисный сервер: Bastion, patch 3, в середине.</summary>
    public static readonly Hex Office = Hex.FromOffset(2, 2);

    /// <summary>Роутер: Bastion, patch 2, между офисом и целью.</summary>
    public static readonly Hex Router = Hex.FromOffset(3, 3);

    /// <summary>Цель: сервер датацентра, Bastion, patch 4, дальний угол (4,4).</summary>
    public static readonly Hex Target = Hex.FromOffset(4, 4);

    /// <summary>Workstation с Bastion: patch 2, для проверки "ОС не совпала".</summary>
    public static readonly Hex BastionDesk = Hex.FromOffset(0, 2);


    /// <summary>
    /// Карта 5 × 5: всё Home/Kestrel/patch 1, кроме нескольких особых узлов (см. поля).
    /// Пустых гексов нет - каждый гекс узел, связность гарантирована.
    /// </summary>
    /// <returns>Вернет карту со стартом и целью.</returns>
    public static MapResult Tiny()
    {
        World world = new(5, 5, factions: 1);
        foreach (Hex hex in world.Cells())
        {
            Set(world, hex, NodeType.Home, Os.Kestrel, patch: 1);
        }
        Set(world, Iot, NodeType.IoT, Os.Mote, patch: 0);
        Set(world, Office, NodeType.Server, Os.Bastion, patch: 3);
        Set(world, Router, NodeType.Router, Os.Bastion, patch: 2);
        Set(world, Target, NodeType.Server, Os.Bastion, patch: 4);
        Set(world, BastionDesk, NodeType.Workstation, Os.Bastion, patch: 2);
        return new MapResult(world, Start, Target, SeedUsed: 0);
    }


    /// <summary>
    /// Метод установки узла.
    /// </summary>
    /// <param name="world">Карта.</param>
    /// <param name="hex">Гекс.</param>
    /// <param name="type">Тип узла.</param>
    /// <param name="os">ОС.</param>
    /// <param name="patch">Защита.</param>
    public static void Set(World world, Hex hex, NodeType type, Os os, int patch)
    {
        world.Type[hex] = type;
        world.Os[hex] = os;
        world.Patch[hex] = patch;
        world.Name[hex] = $"{type} {hex.ToOffset()}";
    }
}
