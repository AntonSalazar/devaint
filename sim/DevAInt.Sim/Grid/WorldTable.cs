using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Grid;

/// <summary>Полы и стены по номерам с единицы, 0 - пусто; строится один раз после загрузки модов.</summary>
public sealed class WorldTable
{
    /// <summary>Полы по номерам, номер 0 не используется.</summary>
    private readonly List<FloorDef?> _floors = [null];

    /// <summary>Стены по номерам, номер 0 не используется.</summary>
    private readonly List<WallDef?> _walls = [null];

    /// <summary>Номер пола по id.</summary>
    private readonly Dictionary<string, ushort> _floorIndex = [];

    /// <summary>Номер стены по id.</summary>
    private readonly Dictionary<string, ushort> _wallIndex = [];

    /// <summary>Таблица по проверенным определениям.</summary>
    public WorldTable(DefDatabase defs)
    {
        foreach (FloorDef floor in defs.All<FloorDef>())
        {
            _floorIndex[floor.Id] = Add(_floors, floor);
        }

        foreach (WallDef wall in defs.All<WallDef>())
        {
            _wallIndex[wall.Id] = Add(_walls, wall);
        }
    }

    /// <summary>Число полов, номера от 1 до FloorCount.</summary>
    public int FloorCount => _floors.Count - 1;

    /// <summary>Число стен, номера от 1 до WallCount.</summary>
    public int WallCount => _walls.Count - 1;

    /// <summary>Пол по номеру.</summary>
    public FloorDef Floor(ushort index) => _floors[index]!;

    /// <summary>Стена по номеру.</summary>
    public WallDef Wall(ushort index) => _walls[index]!;

    /// <summary>Номер пола по id, исключение если его нет.</summary>
    public ushort FloorIndex(string id) => _floorIndex[id];

    /// <summary>Номер стены по id, исключение если её нет.</summary>
    public ushort WallIndex(string id) => _wallIndex[id];

    /// <summary>Номер пола по id, false если его нет.</summary>
    public bool TryFloorIndex(string id, out ushort index) => _floorIndex.TryGetValue(id, out index);

    /// <summary>Номер стены по id, false если её нет.</summary>
    public bool TryWallIndex(string id, out ushort index) => _wallIndex.TryGetValue(id, out index);

    /// <summary>Добавление в список, номер нового элемента.</summary>
    private static ushort Add<T>(List<T?> list, T item)
        where T : class
    {
        if (list.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException($"more than {ushort.MaxValue} {typeof(T).Name} definitions");
        }

        list.Add(item);
        return (ushort)(list.Count - 1);
    }
}
