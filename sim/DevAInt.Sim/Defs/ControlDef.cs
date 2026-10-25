using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DevAInt.Sim.Defs;

/// <summary>Что делает действие управления: поведение ядра или хук Lua мода.</summary>
public enum ControlAction
{
    /// <summary>Прицеливание: пока действие зажато, взгляд идёт за курсором.</summary>
    Aim,

    /// <summary>Удар существа по ближайшему впереди.</summary>
    Attack,

    /// <summary>Толчок ближайшего впереди.</summary>
    Shove,

    /// <summary>Первое применение первого подходящего предмета.</summary>
    UseItem,

    /// <summary>Хук control у модов: id действия и точка курсора.</summary>
    Lua,
}

/// <summary>Действие управления: клавиши по умолчанию, держится ли оно и что делает.</summary>
public sealed partial record ControlDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Клавиши и кнопки по умолчанию: mouse_left, mouse_right, mouse_middle или key_ и имя клавиши.</summary>
    public required IReadOnlyList<string> Keys { get; init; }

    /// <summary>Действие держится: сервер узнаёт и нажатие, и отпускание.</summary>
    public bool Hold { get; init; }

    /// <summary>Другое действие, которое должно быть зажато, например удар только при прицеле.</summary>
    public string? Needs { get; init; }

    /// <summary>Что делает действие.</summary>
    public required ControlAction Does { get; init; }

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Keys.Count == 0)
        {
            yield return "keys must not be empty";
        }

        foreach (string key in Keys)
        {
            if (!KeyRegex().IsMatch(key))
            {
                yield return $"key {key} must be mouse_left, mouse_right, mouse_middle or key_ and a key name";
            }
        }

        if (Needs is not null && (!defs.TryGet<ControlDef>(Needs, out ControlDef? needed) || !needed.Hold))
        {
            yield return $"needs {Needs} must be a hold control";
        }
    }

    [GeneratedRegex("^(mouse_(left|right|middle)|key_[a-z0-9_]+)$")]
    private static partial Regex KeyRegex();
}
