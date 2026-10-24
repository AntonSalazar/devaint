using System.Collections.Generic;
using System.Linq;

namespace DevAInt.Sim.Mods;

/// <summary>Мод для сверки клиента с сервером: id, версия и хеш данных.</summary>
public sealed record ModInfo(string Id, string Version, string Hash)
{
    /// <summary>Чем моды клиента отличаются от модов сервера, null если совпадают вместе с порядком.</summary>
    public static string? Difference(IReadOnlyList<ModInfo> local, IReadOnlyList<ModInfo> server)
    {
        if (local.SequenceEqual(server))
        {
            return null;
        }

        List<string> problems = [];
        foreach (ModInfo mod in server)
        {
            ModInfo? own = local.FirstOrDefault(item => item.Id == mod.Id);
            if (own is null)
            {
                problems.Add($"missing mod {mod.Id} {mod.Version}");
            }
            else if (own.Version != mod.Version)
            {
                problems.Add($"mod {mod.Id} is {own.Version}, server has {mod.Version}");
            }
            else if (own.Hash != mod.Hash)
            {
                problems.Add($"mod {mod.Id} {mod.Version} has different data");
            }
        }

        foreach (ModInfo own in local.Where(item => server.All(mod => mod.Id != item.Id)))
        {
            problems.Add($"extra mod {own.Id}");
        }

        // Те же моды с теми же данными, но в другом порядке: номера определений всё равно разойдутся
        if (problems.Count == 0)
        {
            problems.Add("mod load order differs");
        }

        return "mods differ from the server: " + string.Join("; ", problems);
    }
}
