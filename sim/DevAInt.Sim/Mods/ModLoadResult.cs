using System.Collections.Generic;
using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Mods;

/// <summary>Итог загрузки модов: моды в порядке загрузки, определения и ошибки.</summary>
public sealed record ModLoadResult(IReadOnlyList<ModManifest> Mods, DefDatabase Defs, IReadOnlyList<ModError> Errors)
{
    /// <summary>Загрузка прошла без ошибок.</summary>
    public bool Ok => Errors.Count == 0;
}
