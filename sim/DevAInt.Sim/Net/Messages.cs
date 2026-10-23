using System.Collections.Generic;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Net;

/// <summary>Сообщение сервера клиенту.</summary>
public abstract record ServerMessage;

/// <summary>Подключение принято: своя сущность и размер мира.</summary>
public sealed record WelcomeMessage(long EntityId, int Width, int Height) : ServerMessage;

/// <summary>Снимок чанка: все созданные слои этажей целиком.</summary>
public sealed record ChunkSnapshotMessage(int ChunkX, int ChunkY, IReadOnlyList<ChunkLevelData> Levels) : ServerMessage;

/// <summary>Чанк вышел из зоны клиента, клиент забывает его вместе с сущностями в нём.</summary>
public sealed record ChunkUnloadMessage(int ChunkX, int ChunkY) : ServerMessage;

/// <summary>Сущность появилась у клиента: позиция и уровни всех мудлов.</summary>
public sealed record EntitySpawnMessage(long Id, float X, float Y, int Z, IReadOnlyList<int> MoodleLevels) : ServerMessage;

/// <summary>Сущность сдвинулась.</summary>
public sealed record EntityMoveMessage(long Id, float X, float Y, int Z) : ServerMessage;

/// <summary>Уровень мудла сущности сменился.</summary>
public sealed record MoodleLevelMessage(long Id, int Moodle, int Level) : ServerMessage;

/// <summary>Сущность исчезла у клиента.</summary>
public sealed record EntityRemoveMessage(long Id) : ServerMessage;

/// <summary>Значения всех статов своей сущности.</summary>
public sealed record OwnStatsMessage(IReadOnlyList<float> Values) : ServerMessage;

/// <summary>Пол клетки изменился.</summary>
public sealed record FloorChangeMessage(CellPos Cell, ushort Floor) : ServerMessage;

/// <summary>Северное или западное ребро клетки-владельца изменилось.</summary>
public sealed record EdgeChangeMessage(CellPos Owner, bool North, Edge Edge) : ServerMessage;

/// <summary>Пакет сервера за тик: номер тика и сообщения по порядку.</summary>
public sealed record ServerFrame(long Tick, IReadOnlyList<ServerMessage> Messages);

/// <summary>Ввод клиента: номер по порядку и направление движения.</summary>
public sealed record InputMessage(uint Sequence, float X, float Y);

/// <summary>Один слой этажа чанка в снимке.</summary>
public sealed record ChunkLevelData(int Z, ushort[] Floors, ushort[] NorthWalls, ushort[] WestWalls, ushort[] Flags);
