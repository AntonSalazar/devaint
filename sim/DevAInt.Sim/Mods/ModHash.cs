using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DevAInt.Sim.Mods;

/// <summary>Хеш данных мода, которые читает клиент: манифест и data/*.jsonc.</summary>
public static class ModHash
{
    /// <summary>SHA-256 файлов мода в папке directory строкой из hex-цифр.</summary>
    public static string Compute(string directory)
    {
        // Скрипты не входят: Lua работает только на сервере, клиенту они не нужны
        List<string> files = [Path.Combine(directory, "mod.jsonc")];
        string data = Path.Combine(directory, "data");
        if (Directory.Exists(data))
        {
            string[] defs = Directory.GetFiles(data, "*.jsonc");
            Array.Sort(defs, StringComparer.Ordinal);
            files.AddRange(defs);
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files)
        {
            // Имя и длина перед содержимым: так перенос текста между файлами меняет хеш
            string name = Path.GetRelativePath(directory, file).Replace('\\', '/');
            byte[] content = Encoding.UTF8.GetBytes(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal));
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            hash.AppendData(BitConverter.GetBytes((long)content.Length));
            hash.AppendData(content);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
