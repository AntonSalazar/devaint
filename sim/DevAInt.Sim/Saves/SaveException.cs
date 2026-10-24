using System;

namespace DevAInt.Sim.Saves;

/// <summary>Сохранение нельзя загрузить: файлы повреждены, другой формат или нет нужных модов.</summary>
public sealed class SaveException(string message) : Exception(message);
