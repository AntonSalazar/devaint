using System;
using System.Collections.Generic;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Net;

/// <summary>Запись и чтение пакетов: тип сообщения одним байтом, дальше его поля.</summary>
public static class Protocol
{
    /// <summary>Наибольшее число сообщений в пакете, защита от мусорных данных.</summary>
    private const int MaxMessages = 100_000;

    /// <summary>Наибольшее число значений в списке внутри сообщения.</summary>
    private const int MaxList = 65_536;

    /// <summary>Клеток в слое чанка.</summary>
    private const int ChunkCells = World.ChunkSize * World.ChunkSize;

    /// <summary>Типы сообщений сервера.</summary>
    private enum ServerType : byte
    {
        Welcome = 1,
        ChunkSnapshot,
        ChunkUnload,
        EntitySpawn,
        EntityMove,
        MoodleLevel,
        EntityRemove,
        OwnStats,
        FloorChange,
        EdgeChange,
    }

    /// <summary>Пакет сервера в байты.</summary>
    public static byte[] Write(ServerFrame frame)
    {
        PacketWriter writer = new();
        writer.WriteVarInt(frame.Tick);
        writer.WriteVarUInt((ulong)frame.Messages.Count);
        foreach (ServerMessage message in frame.Messages)
        {
            WriteMessage(writer, message);
        }

        return writer.ToArray();
    }

    /// <summary>Пакет сервера из байт.</summary>
    public static ServerFrame ReadServerFrame(byte[] data)
    {
        PacketReader reader = new(data);
        long tick = reader.ReadVarInt();
        int count = reader.ReadCount(MaxMessages);
        List<ServerMessage> messages = new(count);
        for (int i = 0; i < count; i++)
        {
            messages.Add(ReadMessage(reader));
        }

        RequireEnd(reader);
        return new ServerFrame(tick, messages);
    }

    /// <summary>Ввод клиента в байты.</summary>
    public static byte[] Write(InputMessage input)
    {
        PacketWriter writer = new();
        writer.WriteVarUInt(input.Sequence);
        writer.WriteFloat(input.X);
        writer.WriteFloat(input.Y);
        return writer.ToArray();
    }

    /// <summary>Ввод клиента из байт.</summary>
    public static InputMessage ReadInput(byte[] data)
    {
        PacketReader reader = new(data);
        ulong sequence = reader.ReadVarUInt();
        if (sequence > uint.MaxValue)
        {
            throw new ProtocolException("input sequence is too large");
        }

        InputMessage input = new((uint)sequence, reader.ReadFloat(), reader.ReadFloat());
        RequireEnd(reader);
        return input;
    }

    /// <summary>Одно сообщение сервера с типом.</summary>
    private static void WriteMessage(PacketWriter writer, ServerMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                writer.WriteByte((byte)ServerType.Welcome);
                writer.WriteVarInt(welcome.EntityId);
                writer.WriteVarUInt((ulong)welcome.Width);
                writer.WriteVarUInt((ulong)welcome.Height);
                break;
            case ChunkSnapshotMessage snapshot:
                writer.WriteByte((byte)ServerType.ChunkSnapshot);
                writer.WriteVarUInt((ulong)snapshot.ChunkX);
                writer.WriteVarUInt((ulong)snapshot.ChunkY);
                writer.WriteVarUInt((ulong)snapshot.Levels.Count);
                foreach (ChunkLevelData level in snapshot.Levels)
                {
                    writer.WriteVarInt(level.Z);
                    writer.WriteRuns(level.Floors);
                    writer.WriteRuns(level.NorthWalls);
                    writer.WriteRuns(level.WestWalls);
                    writer.WriteRuns(level.Flags);
                }

                break;
            case ChunkUnloadMessage unload:
                writer.WriteByte((byte)ServerType.ChunkUnload);
                writer.WriteVarUInt((ulong)unload.ChunkX);
                writer.WriteVarUInt((ulong)unload.ChunkY);
                break;
            case EntitySpawnMessage spawn:
                writer.WriteByte((byte)ServerType.EntitySpawn);
                WritePosition(writer, spawn.Id, spawn.X, spawn.Y, spawn.Z);
                writer.WriteVarUInt((ulong)spawn.MoodleLevels.Count);
                foreach (int level in spawn.MoodleLevels)
                {
                    writer.WriteVarUInt((ulong)level);
                }

                break;
            case EntityMoveMessage move:
                writer.WriteByte((byte)ServerType.EntityMove);
                WritePosition(writer, move.Id, move.X, move.Y, move.Z);
                break;
            case MoodleLevelMessage moodle:
                writer.WriteByte((byte)ServerType.MoodleLevel);
                writer.WriteVarInt(moodle.Id);
                writer.WriteVarUInt((ulong)moodle.Moodle);
                writer.WriteVarUInt((ulong)moodle.Level);
                break;
            case EntityRemoveMessage remove:
                writer.WriteByte((byte)ServerType.EntityRemove);
                writer.WriteVarInt(remove.Id);
                break;
            case OwnStatsMessage stats:
                writer.WriteByte((byte)ServerType.OwnStats);
                writer.WriteVarUInt((ulong)stats.Values.Count);
                foreach (float value in stats.Values)
                {
                    writer.WriteFloat(value);
                }

                break;
            case FloorChangeMessage floor:
                writer.WriteByte((byte)ServerType.FloorChange);
                WriteCell(writer, floor.Cell);
                writer.WriteVarUInt(floor.Floor);
                break;
            case EdgeChangeMessage edge:
                writer.WriteByte((byte)ServerType.EdgeChange);
                WriteCell(writer, edge.Owner);
                writer.WriteBool(edge.North);
                writer.WriteVarUInt(edge.Edge.Wall);
                writer.WriteBool(edge.Edge.Open);
                break;
            default:
                throw new ArgumentException($"unknown server message {message.GetType().Name}", nameof(message));
        }
    }

    /// <summary>Одно сообщение сервера по типу.</summary>
    private static ServerMessage ReadMessage(PacketReader reader)
    {
        byte type = reader.ReadByte();
        return (ServerType)type switch
        {
            ServerType.Welcome => new WelcomeMessage(reader.ReadVarInt(), reader.ReadCount(World.MaxSize), reader.ReadCount(World.MaxSize)),
            ServerType.ChunkSnapshot => ReadSnapshot(reader),
            ServerType.ChunkUnload => new ChunkUnloadMessage(reader.ReadCount(MaxList), reader.ReadCount(MaxList)),
            ServerType.EntitySpawn => ReadSpawn(reader),
            ServerType.EntityMove => new EntityMoveMessage(reader.ReadVarInt(), reader.ReadFloat(), reader.ReadFloat(), (int)reader.ReadVarInt()),
            ServerType.MoodleLevel => new MoodleLevelMessage(reader.ReadVarInt(), reader.ReadCount(MaxList), reader.ReadCount(MaxList)),
            ServerType.EntityRemove => new EntityRemoveMessage(reader.ReadVarInt()),
            ServerType.OwnStats => ReadOwnStats(reader),
            ServerType.FloorChange => new FloorChangeMessage(ReadCell(reader), ReadUShort(reader)),
            ServerType.EdgeChange => new EdgeChangeMessage(ReadCell(reader), reader.ReadBool(), new Edge(ReadUShort(reader), reader.ReadBool())),
            _ => throw new ProtocolException($"unknown server message type {type}"),
        };
    }

    /// <summary>Снимок чанка со всеми слоями.</summary>
    private static ChunkSnapshotMessage ReadSnapshot(PacketReader reader)
    {
        int chunkX = reader.ReadCount(MaxList);
        int chunkY = reader.ReadCount(MaxList);
        int count = reader.ReadCount(World.LevelCount);
        List<ChunkLevelData> levels = new(count);
        for (int i = 0; i < count; i++)
        {
            int z = (int)reader.ReadVarInt();
            ChunkLevelData level = new(z, new ushort[ChunkCells], new ushort[ChunkCells], new ushort[ChunkCells], new ushort[ChunkCells]);
            reader.ReadRuns(level.Floors);
            reader.ReadRuns(level.NorthWalls);
            reader.ReadRuns(level.WestWalls);
            reader.ReadRuns(level.Flags);
            levels.Add(level);
        }

        return new ChunkSnapshotMessage(chunkX, chunkY, levels);
    }

    /// <summary>Появление сущности с уровнями мудлов.</summary>
    private static EntitySpawnMessage ReadSpawn(PacketReader reader)
    {
        long id = reader.ReadVarInt();
        float x = reader.ReadFloat();
        float y = reader.ReadFloat();
        int z = (int)reader.ReadVarInt();
        int count = reader.ReadCount(MaxList);
        int[] levels = new int[count];
        for (int i = 0; i < count; i++)
        {
            levels[i] = reader.ReadCount(MaxList);
        }

        return new EntitySpawnMessage(id, x, y, z, levels);
    }

    /// <summary>Статы своей сущности.</summary>
    private static OwnStatsMessage ReadOwnStats(PacketReader reader)
    {
        int count = reader.ReadCount(MaxList);
        float[] values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = reader.ReadFloat();
        }

        return new OwnStatsMessage(values);
    }

    /// <summary>Id и позиция сущности.</summary>
    private static void WritePosition(PacketWriter writer, long id, float x, float y, int z)
    {
        writer.WriteVarInt(id);
        writer.WriteFloat(x);
        writer.WriteFloat(y);
        writer.WriteVarInt(z);
    }

    /// <summary>Клетка тремя числами.</summary>
    private static void WriteCell(PacketWriter writer, CellPos cell)
    {
        writer.WriteVarInt(cell.X);
        writer.WriteVarInt(cell.Y);
        writer.WriteVarInt(cell.Z);
    }

    /// <summary>Клетка тремя числами.</summary>
    private static CellPos ReadCell(PacketReader reader) => new((int)reader.ReadVarInt(), (int)reader.ReadVarInt(), (int)reader.ReadVarInt());

    /// <summary>Число, которое должно поместиться в ushort.</summary>
    private static ushort ReadUShort(PacketReader reader) => (ushort)reader.ReadCount(ushort.MaxValue);

    /// <summary>После последнего поля в пакете не должно остаться байт.</summary>
    private static void RequireEnd(PacketReader reader)
    {
        if (!reader.AtEnd)
        {
            throw new ProtocolException("packet has trailing bytes");
        }
    }
}
