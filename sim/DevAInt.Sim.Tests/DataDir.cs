using System;
using System.IO;

namespace DevAInt.Sim.Tests;

/// <summary>
/// Поиск папки данных игры (`project/data`) относительно бинарника тестов:
/// поднимаемся вверх, пока не найдём корень репозитория.
/// </summary>
public static class DataDir
{
    /// <summary>Абсолютный путь к `project/data`.</summary>
    public static string Path { get; } = Find();

    /// <summary>Временная копия папки данных для тестов с «испорченными» файлами.</summary>
    /// <returns>Путь к копии; вызывающий удаляет её сам.</returns>
    public static string CopyToTemp()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devaint-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (string file in Directory.GetFiles(Path, "*.json"))
        {
            File.Copy(file, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(file)));
        }
        return dir;
    }

    /// <summary>Подъём от папки бинарника до каталога, содержащего `project/data`.</summary>
    /// <returns>Путь к данным.</returns>
    private static string Find()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            string candidate = System.IO.Path.Combine(cursor.FullName, "project", "data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            cursor = cursor.Parent;
        }
        throw new DirectoryNotFoundException("project/data not found above " + AppContext.BaseDirectory);
    }
}
