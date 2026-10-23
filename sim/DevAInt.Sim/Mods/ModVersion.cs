using System;
using System.Globalization;

namespace DevAInt.Sim.Mods;

/// <summary>Версия мода major.minor.patch.</summary>
public readonly record struct ModVersion(int Major, int Minor, int Patch) : IComparable<ModVersion>
{
    /// <summary>Разбор строки 0.1 или 0.1.0, false при неверном формате.</summary>
    public static bool TryParse(string text, out ModVersion version)
    {
        version = default;
        string[] parts = text.Split('.');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        // Недостающий patch остаётся нулём: 0.1 означает 0.1.0
        int[] numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new ModVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    /// <summary>Сравнение по major, затем minor, затем patch.</summary>
    public int CompareTo(ModVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        return Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);
    }

    /// <summary>Строка вида 0.1.0.</summary>
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
