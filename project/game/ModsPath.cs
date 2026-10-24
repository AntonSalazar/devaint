using System.IO;
using Godot;

/// <summary>Папки модов и сохранений: в редакторе - рядом с project, в экспорте - рядом с исполняемым файлом.</summary>
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

    /// <summary>Папка сохранений рядом с папкой модов.</summary>
    public static string SavesRoot() => Path.Combine(Path.GetDirectoryName(Find())!, "saves");
}
