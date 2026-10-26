using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Grid;

/// <summary>Полы, стены и объекты по номерам с единицы, 0 - пусто; строится один раз после загрузки модов.</summary>
public sealed class WorldTable
{
    /// <summary>Полы по номерам, номер 0 не используется.</summary>
    private readonly List<FloorDef?> _floors = [null];

    /// <summary>Стены по номерам, номер 0 не используется.</summary>
    private readonly List<WallDef?> _walls = [null];

    /// <summary>Створки, двери и окна, по номерам, номер 0 не используется.</summary>
    private readonly List<FillDef?> _fills = [null];

    /// <summary>Номер створки по id.</summary>
    private readonly Dictionary<string, ushort> _fillIndex = [];

    /// <summary>Объекты по номерам, номер 0 не используется.</summary>
    private readonly List<ObjectDef?> _objects = [null];

    /// <summary>Номер пола по id.</summary>
    private readonly Dictionary<string, ushort> _floorIndex = [];

    /// <summary>Номер стены по id.</summary>
    private readonly Dictionary<string, ushort> _wallIndex = [];

    /// <summary>Номер объекта по id.</summary>
    private readonly Dictionary<string, ushort> _objectIndex = [];

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

        foreach (FillDef fill in defs.All<FillDef>())
        {
            _fillIndex[fill.Id] = Add(_fills, fill);
        }

        foreach (ObjectDef obj in defs.All<ObjectDef>())
        {
            _objectIndex[obj.Id] = Add(_objects, obj);
        }
    }

    /// <summary>Число полов, номера от 1 до FloorCount.</summary>
    public int FloorCount => _floors.Count - 1;

    /// <summary>Число стен, номера от 1 до WallCount.</summary>
    public int WallCount => _walls.Count - 1;

    /// <summary>Число створок, номера от 1 до FillCount.</summary>
    public int FillCount => _fills.Count - 1;

    /// <summary>Число объектов, номера от 1 до ObjectCount.</summary>
    public int ObjectCount => _objects.Count - 1;

    /// <summary>Створка по номеру.</summary>
    public FillDef Fill(ushort index) => _fills[index]!;

    /// <summary>Номер створки по id, исключение если её нет.</summary>
    public ushort FillIndex(string id) => _fillIndex[id];

    /// <summary>Номер створки по id, false если её нет.</summary>
    public bool TryFillIndex(string id, out ushort index) => _fillIndex.TryGetValue(id, out index);

    /// <summary>Пол по номеру.</summary>
    public FloorDef Floor(ushort index) => _floors[index]!;

    /// <summary>Стена по номеру.</summary>
    public WallDef Wall(ushort index) => _walls[index]!;

    /// <summary>Объект по номеру.</summary>
    public ObjectDef Object(ushort index) => _objects[index]!;

    /// <summary>Номер объекта по id, исключение если его нет.</summary>
    public ushort ObjectIndex(string id) => _objectIndex[id];

    /// <summary>Номер объекта по id, false если его нет.</summary>
    public bool TryObjectIndex(string id, out ushort index) => _objectIndex.TryGetValue(id, out index);

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
