using System;
using System.IO;
using DevAInt.Sim.Saves;

/// <summary>Постоянный id игрока на этой машине: создаётся один раз и хранится в файле.</summary>
public static class PlayerIdentity
{
    /// <summary>Файл id в папке данных пользователя Godot.</summary>
    public const string DefaultPath = "user://player_id";

    /// <summary>Id из файла path или новый случайный, записанный в этот файл.</summary>
    public static string LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            string saved = File.ReadAllText(path).Trim();
            if (SaveGame.IsValidName(saved))
            {
                return saved;
            }
        }

        // Испорченный файл заменяется новым id: персонаж прежнего id останется в сохранении
        string id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, id);
        return id;
    }
}
