using System;
using System.Collections.Generic;
using System.Linq;

namespace DevAInt.Sim.Mods;

/// <summary>Порядок загрузки модов по depends и loadAfter.</summary>
internal static class ModOrder
{
    /// <summary>Моды в порядке загрузки, зависимости раньше зависящих; проблемы попадают в errors.</summary>
    public static List<ModManifest> Sort(IReadOnlyList<ModManifest> mods, List<ModError> errors)
    {
        Dictionary<string, ModManifest> byId = mods.ToDictionary(mod => mod.Id);

        // Для каждого мода - моды, которые должны загрузиться раньше него
        Dictionary<string, HashSet<string>> before = [];
        foreach (ModManifest mod in mods)
        {
            before[mod.Id] = [];
            for (int i = 0; i < mod.Depends.Count; i++)
            {
                ModDependency dependency = mod.Depends[i];
                string path = $"$.depends[{i}]";
                if (!byId.TryGetValue(dependency.ModId, out ModManifest? found))
                {
                    errors.Add(new ModError(mod.File, null, path, $"mod {dependency.ModId} not found"));
                    continue;
                }

                if (!dependency.IsSatisfiedBy(found.Version))
                {
                    string message = $"requires {dependency.ModId} {dependency.MinVersion} or newer, found {found.Version}";
                    errors.Add(new ModError(mod.File, null, path, message));
                }

                before[mod.Id].Add(dependency.ModId);
            }

            // loadAfter только упорядочивает и не требует наличия мода
            before[mod.Id].UnionWith(mod.LoadAfter.Where(byId.ContainsKey));
        }

        // Топологическая сортировка; из готовых к загрузке берём меньший id, чтобы порядок не зависел от случая
        List<ModManifest> ordered = [];
        SortedSet<string> ready = new(before.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key), StringComparer.Ordinal);
        while (ready.Count > 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            ordered.Add(byId[id]);
            foreach ((string other, HashSet<string> waits) in before)
            {
                if (waits.Remove(id) && waits.Count == 0)
                {
                    ready.Add(other);
                }
            }
        }

        if (ordered.Count < mods.Count)
        {
            string cycle = string.Join(", ", mods.Select(mod => mod.Id).Except(ordered.Select(mod => mod.Id)).Order(StringComparer.Ordinal));
            errors.Add(new ModError(string.Empty, null, string.Empty, $"circular mod dependency: {cycle}"));
        }

        return ordered;
    }
}
