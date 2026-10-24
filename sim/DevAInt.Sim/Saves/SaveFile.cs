using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DevAInt.Sim.Saves;

/// <summary>Файлы сохранения: атомарная запись и двоичные файлы с меткой, версией и сжатием.</summary>
internal static class SaveFile
{
    /// <summary>Длина метки вида файла в начале двоичного файла.</summary>
    private const int MagicLength = 4;

    /// <summary>Двоичный файл: метка, версия формата и сжатые данные.</summary>
    public static void WriteBinary(string path, string magic, byte[] payload)
    {
        WriteAtomic(path, file =>
        {
            file.Write(Encoding.ASCII.GetBytes(magic));
            file.WriteByte(SaveGame.FormatVersion);
            using ZLibStream zlib = new(file, CompressionLevel.Fastest, leaveOpen: true);
            zlib.Write(payload);
        });
    }

    /// <summary>Данные двоичного файла; чужая метка, версия или повреждённое сжатие - SaveException.</summary>
    public static byte[] ReadBinary(string path, string magic)
    {
        string name = Path.GetFileName(path);
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length <= MagicLength || Encoding.ASCII.GetString(bytes, 0, MagicLength) != magic)
        {
            throw new SaveException($"{name}: not a {magic} save file");
        }

        if (bytes[MagicLength] != SaveGame.FormatVersion)
        {
            throw new SaveException($"{name}: save format {bytes[MagicLength]} is not supported, expected {SaveGame.FormatVersion}");
        }

        try
        {
            using MemoryStream source = new(bytes, MagicLength + 1, bytes.Length - MagicLength - 1);
            using ZLibStream zlib = new(source, CompressionMode.Decompress);
            using MemoryStream result = new();
            zlib.CopyTo(result);
            return result.ToArray();
        }
        catch (InvalidDataException)
        {
            throw new SaveException($"{name}: data is corrupted");
        }
    }

    /// <summary>Текстовый файл UTF-8, записанный атомарно.</summary>
    public static void WriteText(string path, string text) => WriteAtomic(path, file => file.Write(Encoding.UTF8.GetBytes(text)));

    /// <summary>Запись во временный файл и замена им старого: падение посреди записи не портит старый файл.</summary>
    private static void WriteAtomic(string path, Action<FileStream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (FileStream file = File.Create(temp))
        {
            write(file);

            // Данные на диске до переименования, иначе после сбоя питания новый файл может оказаться пустым
            file.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }
}
