using System.Collections.Generic;

namespace DevAInt.Sim.Mods;

/// <summary>Проверенный манифест мода; Directory - полный путь папки, File - путь манифеста для сообщений.</summary>
public sealed record ModManifest(
    string Id,
    string Name,
    ModVersion Version,
    IReadOnlyList<ModDependency> Depends,
    IReadOnlyList<string> LoadAfter,
    string Directory,
    string File);
