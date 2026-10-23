using System;
using System.IO;
using System.Linq;
using DevAInt.Sim.Mods;

namespace DevAInt.Sim.Tests.Mods;

/// <summary>Временная папка mods для теста, удаляется в Dispose.</summary>
public sealed class TempMods : IDisposable
{
    /// <summary>Создаёт пустую папку mods во временном каталоге.</summary>
    public TempMods()
    {
        Root = Path.Combine(Path.GetTempPath(), "devaint-tests-" + Guid.NewGuid().ToString("N"), "mods");
        Directory.CreateDirectory(Root);
    }

    /// <summary>Полный путь папки mods.</summary>
    public string Root { get; }

    /// <summary>Записывает файл по пути относительно mods.</summary>
    public TempMods Write(string relativePath, string text)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return this;
    }

    /// <summary>Записывает манифест мода с API ядра и зависимостями.</summary>
    public TempMods Manifest(string id, string version = "0.1.0", string depends = "")
    {
        string list = string.Join(", ", depends.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(item => $"\"{item.Trim()}\""));
        return Write($"{id}/mod.jsonc", $$"""{ "id": "{{id}}", "name": "{{id}}", "version": "{{version}}", "api": {{ModApi.Version}}, "depends": [{{list}}] }""");
    }

    /// <summary>Копирует настоящий мод из папки mods репозитория.</summary>
    public TempMods CopyRepoMod(string id)
    {
        string source = Path.Combine(RepoMods.Root, id);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            Write(Path.Combine(id, Path.GetRelativePath(source, file)), File.ReadAllText(file));
        }

        return this;
    }

    /// <summary>Загружает перечисленные моды.</summary>
    public ModLoadResult Load(params string[] modIds) => ModLoader.Load(Root, modIds);

    /// <summary>Удаляет временный каталог.</summary>
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
}
