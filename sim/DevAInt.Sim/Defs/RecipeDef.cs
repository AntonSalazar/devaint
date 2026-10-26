using System.Collections.Generic;
using System.Linq;

namespace DevAInt.Sim.Defs;

/// <summary>Рецепт крафта, как в Project Zomboid: что тратится, какие инструменты нужны, что получается, сколько секунд и каким действием.</summary>
public sealed record RecipeDef : IDef
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <summary>Предметы, которые тратятся.</summary>
    public IReadOnlyList<ItemCountDef> Inputs { get; init; } = [];

    /// <summary>Инструменты: нужны в инвентаре, но остаются.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>Что получается.</summary>
    public required IReadOnlyList<ItemCountDef> Outputs { get; init; }

    /// <summary>Длительность в секундах симуляции.</summary>
    public required double Seconds { get; init; }

    /// <summary>Действие, которым идёт крафт: от него правило прерывания движением.</summary>
    public string Action { get; init; } = "core:craft";

    /// <inheritdoc/>
    public IEnumerable<string> Validate(DefDatabase defs)
    {
        if (Outputs.Count == 0)
        {
            yield return "outputs must not be empty";
        }

        if (Seconds <= 0)
        {
            yield return $"seconds {Seconds} must be positive";
        }

        if (!defs.TryGet<ActionDef>(Action, out _))
        {
            yield return $"unknown action {Action}";
        }

        foreach (string problem in ItemCountDef.Validate(Inputs, "inputs", defs).Concat(ItemCountDef.Validate(Outputs, "outputs", defs)))
        {
            yield return problem;
        }

        foreach (string tool in Tools)
        {
            if (!defs.TryGet<ItemDef>(tool, out _))
            {
                yield return $"tools: unknown item {tool}";
            }

            if (Inputs.Any(input => input.Item == tool))
            {
                yield return $"tools: {tool} is also an input";
            }
        }
    }
}

/// <summary>Предмет и сколько штук: вход или выход рецепта, материал постройки.</summary>
public sealed record ItemCountDef
{
    /// <summary>Id предмета.</summary>
    public required string Item { get; init; }

    /// <summary>Сколько штук.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Проблемы списка: неизвестный предмет, число меньше одного, предмет дважды.</summary>
    internal static IEnumerable<string> Validate(IReadOnlyList<ItemCountDef> list, string name, DefDatabase defs)
    {
        for (int i = 0; i < list.Count; i++)
        {
            ItemCountDef entry = list[i];
            if (!defs.TryGet<ItemDef>(entry.Item, out _))
            {
                yield return $"{name}[{i}]: unknown item {entry.Item}";
            }

            if (entry.Count < 1)
            {
                yield return $"{name}[{i}]: count {entry.Count} must be at least 1";
            }

            if (list.Count(other => other.Item == entry.Item) > 1)
            {
                yield return $"{name}[{i}]: item {entry.Item} is listed twice";
            }
        }
    }
}
