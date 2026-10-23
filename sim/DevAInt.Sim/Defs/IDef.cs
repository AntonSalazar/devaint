using System.Collections.Generic;

namespace DevAInt.Sim.Defs;

/// <summary>Определение из данных мода с уникальным id вида mod:name.</summary>
public interface IDef
{
    /// <summary>Уникальный id определения.</summary>
    string Id { get; }

    /// <summary>Проблемы определения после загрузки всех модов, пусто если всё верно.</summary>
    IEnumerable<string> Validate(DefDatabase defs);
}
