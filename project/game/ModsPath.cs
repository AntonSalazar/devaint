using System.IO;
using Godot;

/// <summary>Папка модов: в редакторе - mods рядом с project, в экспорте - mods рядом с исполняемым файлом.</summary>
public static class ModsPath
{
    /// <summary>Полный путь папки модов.</summary>
    public static string Find()
    {
        string root = OS.HasFeature("editor")
            ? Path.Combine(ProjectSettings.GlobalizePath("res://"), "..")
            : Path.GetDirectoryName(OS.GetExecutablePath())!;
        return Path.GetFullPath(Path.Combine(root, "mods"));
    }
}
