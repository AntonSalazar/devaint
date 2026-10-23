using System.Globalization;
using System.Text.Json.Nodes;

namespace DevAInt.Sim.Mods;

/// <summary>Применение патча с путём к дереву JSON одного определения.</summary>
internal static class PatchApplier
{
    /// <summary>Применяет set, add или remove по пути, false и текст ошибки, если путь не подходит.</summary>
    public static bool TryApply(JsonObject def, PatchDef patch, out string error)
    {
        string[] segments = patch.Path!.Split('.');

        // Спускаемся до родителя последнего сегмента
        JsonNode parent = def;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            JsonNode? child = Child(parent, segments[i]);
            if (child is null)
            {
                error = $"path {patch.Path} has no element {segments[i]}";
                return false;
            }

            parent = child;
        }

        string last = segments[^1];
        error = patch.Op switch
        {
            PatchOp.Set => Set(parent, last, patch.Value!.DeepClone()),
            PatchOp.Add => Add(parent, last, patch.Value!.DeepClone()),
            PatchOp.Remove => Remove(parent, last),
            _ => throw new System.ArgumentOutOfRangeException(nameof(patch), patch.Op, "unknown operation"),
        };

        if (error.Length > 0)
        {
            error = $"path {patch.Path}: {error}";
            return false;
        }

        return true;
    }

    /// <summary>Замена поля объекта или существующего элемента списка, пустая строка при успехе.</summary>
    private static string Set(JsonNode parent, string segment, JsonNode value)
    {
        if (parent is JsonObject obj)
        {
            obj[segment] = value;
            return string.Empty;
        }

        if (parent is not JsonArray list)
        {
            return "value is neither an object nor a list";
        }

        int index = IndexOf(list, segment);
        if (index < 0)
        {
            return $"no element {segment}";
        }

        list[index] = value;
        return string.Empty;
    }

    /// <summary>Добавление значения в конец списка, пустая строка при успехе.</summary>
    private static string Add(JsonNode parent, string segment, JsonNode value)
    {
        if (Child(parent, segment) is not JsonArray list)
        {
            return $"{segment} is not a list";
        }

        list.Add(value);
        return string.Empty;
    }

    /// <summary>Удаление поля объекта или элемента списка, пустая строка при успехе.</summary>
    private static string Remove(JsonNode parent, string segment)
    {
        if (parent is JsonObject obj)
        {
            return obj.Remove(segment) ? string.Empty : $"no field {segment}";
        }

        if (parent is not JsonArray list)
        {
            return "value is neither an object nor a list";
        }

        int index = IndexOf(list, segment);
        if (index < 0)
        {
            return $"no element {segment}";
        }

        list.RemoveAt(index);
        return string.Empty;
    }

    /// <summary>Дочерний узел по имени поля, номеру или id элемента, null если его нет.</summary>
    private static JsonNode? Child(JsonNode node, string segment)
    {
        return node switch
        {
            JsonObject obj => obj[segment],
            JsonArray list => IndexOf(list, segment) is int index and >= 0 ? list[index] : null,
            _ => null,
        };
    }

    /// <summary>Номер элемента списка по номеру или по полю id, -1 если не найден.</summary>
    private static int IndexOf(JsonArray list, string segment)
    {
        if (int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return number < list.Count ? number : -1;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is JsonObject item && item["id"] is JsonValue id && id.TryGetValue(out string? text) && text == segment)
            {
                return i;
            }
        }

        return -1;
    }
}
