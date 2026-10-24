using System.IO;
using DevAInt.Sim;
using DevAInt.Sim.Logging;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Saves;

/// <summary>Мир сервера: загрузка сохранения из --save или новый мир на карте, ошибки - в лог.</summary>
public static class WorldStart
{
    /// <summary>Sim и его сохранение в папке savesRoot; null при ошибке, она уже в логе.</summary>
    public static (Simulation Sim, SaveGame Save)? Open(ModLoadResult mods, ISimLog log, string[] args, string savesRoot)
    {
        string name = GameArgs.SaveName(args);
        if (!SaveGame.IsValidName(name))
        {
            log.Error($"save name {name} must be 1-64 latin letters, digits, - or _");
            return null;
        }

        SaveGame save = new(Path.Combine(savesRoot, name));
        Simulation sim;
        try
        {
            bool exists = save.Exists;
            sim = save.LoadOrCreate(mods, log, GameRoot.MapId, GameArgs.TimeScale(args));
            log.Info(exists ? $"loaded save {name} at tick {sim.Clock.Tick}" : $"new world {name} on map {GameRoot.MapId}");
        }
        catch (SaveException error)
        {
            log.Error($"cannot load save {name}: {error.Message}");
            return null;
        }

        foreach (ModError error in sim.ScriptErrors)
        {
            log.Error(error.ToString());
        }

        return (sim, save);
    }
}
