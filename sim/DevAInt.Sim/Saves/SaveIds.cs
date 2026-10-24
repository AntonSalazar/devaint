using System.Collections.Generic;

namespace DevAInt.Sim.Saves;

/// <summary>Таблица id сохранения: только пополняется, поэтому старые файлы остаются верными при смене модов.</summary>
internal sealed class SaveIds
{
    /// <summary>Таблица из меты или пустая для нового сохранения.</summary>
    public SaveIds(SaveIdLists? lists = null)
    {
        lists ??= new SaveIdLists();
        Floors = new SaveIdList(lists.Floors, first: 1);
        Walls = new SaveIdList(lists.Walls, first: 1);
        Stats = new SaveIdList(lists.Stats, first: 0);
        Items = new SaveIdList(lists.Items, first: 0);
        Actions = new SaveIdList(lists.Actions, first: 0);
        Creatures = new SaveIdList(lists.Creatures, first: 1);
    }

    /// <summary>Полы, номера с единицы.</summary>
    public SaveIdList Floors { get; }

    /// <summary>Стены, номера с единицы.</summary>
    public SaveIdList Walls { get; }

    /// <summary>Статы.</summary>
    public SaveIdList Stats { get; }

    /// <summary>Предметы.</summary>
    public SaveIdList Items { get; }

    /// <summary>Действия.</summary>
    public SaveIdList Actions { get; }

    /// <summary>Виды существ, номера с единицы.</summary>
    public SaveIdList Creatures { get; }

    /// <summary>Списки для меты.</summary>
    public SaveIdLists ToLists() => new()
    {
        Floors = [.. Floors.Ids],
        Walls = [.. Walls.Ids],
        Stats = [.. Stats.Ids],
        Items = [.. Items.Ids],
        Actions = [.. Actions.Ids],
        Creatures = [.. Creatures.Ids],
    };
}

/// <summary>Id одного вида и их номера в файлах сохранения.</summary>
internal sealed class SaveIdList
{
    /// <summary>Id по местам.</summary>
    private readonly List<string> _ids;

    /// <summary>Место по id.</summary>
    private readonly Dictionary<string, int> _places = [];

    /// <summary>Номер первого id: у полов и стен 0 занят пустотой.</summary>
    private readonly int _first;

    /// <summary>Список из меты; first - номер первого id.</summary>
    public SaveIdList(List<string> ids, int first)
    {
        _ids = [.. ids];
        _first = first;
        for (int i = 0; i < _ids.Count; i++)
        {
            if (!_places.TryAdd(_ids[i], i))
            {
                throw new SaveException($"meta.json: id {_ids[i]} is listed twice");
            }
        }
    }

    /// <summary>Id по местам.</summary>
    public IReadOnlyList<string> Ids => _ids;

    /// <summary>Номер id в файлах; новый id добавляется в конец.</summary>
    public int NumberOf(string id)
    {
        if (!_places.TryGetValue(id, out int place))
        {
            place = _ids.Count;
            _ids.Add(id);
            _places[id] = place;
        }

        return place + _first;
    }

    /// <summary>Id по номеру из файла; номер вне таблицы - повреждённые данные.</summary>
    public string IdAt(int number)
    {
        int place = number - _first;
        if (place < 0 || place >= _ids.Count)
        {
            throw new SaveException($"id number {number} is not in the save id table");
        }

        return _ids[place];
    }
}
