namespace DevAInt.Sim.Mods;

/// <summary>Ошибка загрузки модов: файл, строка, путь в JSON и текст.</summary>
public sealed record ModError(string File, int? Line, string JsonPath, string Message)
{
    /// <summary>Строка вида mods/core/data/stats.jsonc:7: $.stats[0].rate: текст.</summary>
    public override string ToString()
    {
        if (File.Length == 0)
        {
            return Message;
        }

        string line = Line is int number ? $":{number}" : string.Empty;
        string path = JsonPath.Length > 0 ? $" {JsonPath}:" : string.Empty;
        return $"{File}{line}:{path} {Message}";
    }
}
