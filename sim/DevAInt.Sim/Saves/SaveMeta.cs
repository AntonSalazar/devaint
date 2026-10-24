using System.Collections.Generic;
using DevAInt.Sim.Mods;

namespace DevAInt.Sim.Saves;

/// <summary>Мета сохранения в meta.json: формат, карта, время, счётчики id, моды и таблица id.</summary>
internal sealed record SaveMeta
{
    /// <summary>Версия формата сохранения.</summary>
    public required int Format { get; init; }

    /// <summary>Карта, из которой начат мир.</summary>
    public required string MapId { get; init; }

    /// <summary>Ширина мира в клетках.</summary>
    public required int Width { get; init; }

    /// <summary>Высота мира в клетках.</summary>
    public required int Height { get; init; }

    /// <summary>Номер тика.</summary>
    public required long Tick { get; init; }

    /// <summary>Id следующей сущности.</summary>
    public required long NextEntityId { get; init; }

    /// <summary>Id следующего предмета.</summary>
    public required long NextItemId { get; init; }

    /// <summary>Моды на момент сохранения, для предупреждений.</summary>
    public required List<ModInfo> Mods { get; init; }

    /// <summary>Таблица id: номера в двоичных файлах.</summary>
    public required SaveIdLists Ids { get; init; }
}

/// <summary>Списки id по видам: номер в файле - место в списке.</summary>
internal sealed record SaveIdLists
{
    /// <summary>Полы, номер в файле на единицу больше места: 0 - пола нет.</summary>
    public List<string> Floors { get; init; } = [];

    /// <summary>Стены, номер в файле на единицу больше места: 0 - стены нет.</summary>
    public List<string> Walls { get; init; } = [];

    /// <summary>Статы.</summary>
    public List<string> Stats { get; init; } = [];

    /// <summary>Предметы.</summary>
    public List<string> Items { get; init; } = [];

    /// <summary>Действия.</summary>
    public List<string> Actions { get; init; } = [];
}
