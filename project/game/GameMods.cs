using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;

/// <summary>Загрузка модов игры с выводом ошибок в лог.</summary>
public static class GameMods
{
    /// <summary>Моды, которые грузятся сейчас; выбор модов появится позже.</summary>
    public static readonly string[] Ids = ["core"];

    /// <summary>Загруженные моды или null, если были ошибки, они уже в логе.</summary>
    public static ModLoadResult? Load(ISimLog log)
    {
        ModLoadResult loaded = ModLoader.Load(ModsPath.Find(), Ids);
        foreach (ModError error in loaded.Errors)
        {
            log.Error(error.ToString());
        }

        return loaded.Ok ? loaded : null;
    }
}
