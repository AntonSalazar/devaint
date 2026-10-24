using System.Collections.Generic;

namespace DevAInt.Sim.Mods;

/// <summary>Проверенный манифест мода; Directory - полный путь папки, File - путь манифеста для сообщений, Hash - хеш данных.</summary>
public sealed record ModManifest(
    string Id,
    string Name,
    ModVersion Version,
    IReadOnlyList<ModDependency> Depends,
    IReadOnlyList<string> LoadAfter,
    string Directory,
    string File,
    string Hash)
{
    /// <summary>Сведения для сверки с сервером.</summary>
    public ModInfo Info => new(Id, Version.ToString(), Hash);
}
