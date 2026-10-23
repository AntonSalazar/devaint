using System.Text.Json.Nodes;

namespace DevAInt.Sim.Mods;

/// <summary>Операция патча.</summary>
public enum PatchOp
{
    /// <summary>Заменить или добавить поле, заменить элемент списка.</summary>
    Set,

    /// <summary>Добавить элемент в конец списка.</summary>
    Add,

    /// <summary>Удалить поле, элемент списка или всё определение.</summary>
    Remove,
}

/// <summary>Патч определения: операция, id цели, путь и значение.</summary>
internal sealed record PatchDef
{
    /// <summary>Операция.</summary>
    public required PatchOp Op { get; init; }

    /// <summary>Id определения, которое меняется.</summary>
    public required string Target { get; init; }

    /// <summary>Путь через точку, элемент списка - по номеру или по id; без пути remove удаляет определение.</summary>
    public string? Path { get; init; }

    /// <summary>Новое значение для set и add.</summary>
    public JsonNode? Value { get; init; }
}
