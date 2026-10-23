using System;

namespace DevAInt.Sim.Mods;

/// <summary>Зависимость мода: id и, если задана, минимальная версия.</summary>
public sealed record ModDependency(string ModId, ModVersion? MinVersion)
{
    /// <summary>Разбор строки core или core >= 0.1, null при неверном формате.</summary>
    public static ModDependency? Parse(string text)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && ModIds.IsModId(parts[0]))
        {
            return new ModDependency(parts[0], null);
        }

        bool valid = parts.Length == 3 && ModIds.IsModId(parts[0]) && parts[1] == ">=";
        if (!valid || !ModVersion.TryParse(parts[2], out ModVersion min))
        {
            return null;
        }

        return new ModDependency(parts[0], min);
    }

    /// <summary>Подходит ли версия найденного мода.</summary>
    public bool IsSatisfiedBy(ModVersion version) => MinVersion is not ModVersion min || version.CompareTo(min) >= 0;
}
