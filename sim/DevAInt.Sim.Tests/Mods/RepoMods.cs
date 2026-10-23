using System;
using System.IO;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Папка mods репозитория, найденная подъёмом от папки сборки тестов.</summary>
public static class RepoMods
{
    /// <summary>Полный путь папки mods.</summary>
    public static string Root { get; } = Find();

    /// <summary>Поиск каталога, в котором есть mods/core/mod.jsonc.</summary>
    private static string Find()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "mods");
            if (File.Exists(Path.Combine(candidate, "core", "mod.jsonc")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("mods/core/mod.jsonc not found above " + AppContext.BaseDirectory);
    }
}
