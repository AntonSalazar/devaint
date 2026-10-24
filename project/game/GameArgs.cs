using System;
using System.Globalization;

/// <summary>Аргументы командной строки игры и сервера.</summary>
public static class GameArgs
{
    /// <summary>Порт сервера по умолчанию.</summary>
    public const int DefaultPort = 7777;

    /// <summary>Имя сохранения по умолчанию.</summary>
    public const string DefaultSave = "world";

    /// <summary>Ускорение игрового времени из --time-scale N, по умолчанию 1.</summary>
    public static double TimeScale(string[] args) => Number(args, "--time-scale") is double scale && scale > 0 ? scale : 1;

    /// <summary>Порт сервера из --port N, по умолчанию DefaultPort.</summary>
    public static int Port(string[] args) => ParsePort(Value(args, "--port")) ?? DefaultPort;

    /// <summary>Адрес сервера из --connect host или --connect host:port, null для игры без сети.</summary>
    public static (string Host, int Port)? Connect(string[] args)
    {
        string? value = Value(args, "--connect");
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // Порт после единственного двоеточия; в адресе IPv6 двоеточий много, и он целиком считается хостом
        int colon = value.IndexOf(':');
        bool single = colon > 0 && colon == value.LastIndexOf(':');
        if (single && ParsePort(value[(colon + 1)..]) is int port)
        {
            return (value[..colon], port);
        }

        return (value, DefaultPort);
    }

    /// <summary>Искусственная задержка сети туда-обратно в миллисекундах из --lag N, по умолчанию 0.</summary>
    public static double LagMs(string[] args) => Number(args, "--lag") is double lag && lag > 0 ? lag : 0;

    /// <summary>Имя сохранения из --save NAME, по умолчанию DefaultSave; проверяет тот, кто открывает сохранение.</summary>
    public static string SaveName(string[] args) => Value(args, "--save") ?? DefaultSave;

    /// <summary>Id игрока из --player ID, null - взять свой постоянный id; нужен, чтобы запустить двух клиентов на одной машине.</summary>
    public static string? Player(string[] args) => Value(args, "--player");

    /// <summary>Значение после имени аргумента или null.</summary>
    private static string? Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>Число после имени аргумента или null.</summary>
    private static double? Number(string[] args, string name)
    {
        bool parsed = double.TryParse(Value(args, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
        return parsed && double.IsFinite(value) ? value : null;
    }

    /// <summary>Порт от 1 до 65535 или null.</summary>
    private static int? ParsePort(string? text) => int.TryParse(text, out int port) && port is > 0 and <= 65_535 ? port : null;
}
