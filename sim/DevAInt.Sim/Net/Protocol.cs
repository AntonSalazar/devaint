using System;
using System.Collections.Generic;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;

namespace DevAInt.Sim.Net;

/// <summary>Запись и чтение пакетов: тип сообщения одним байтом, дальше его поля.</summary>
public static class Protocol
{
    /// <summary>Наибольшее число сообщений в пакете, защита от мусорных данных.</summary>
    private const int MaxMessages = 100_000;

    /// <summary>Наибольшее число значений в списке внутри сообщения.</summary>
    private const int MaxList = 65_536;

    /// <summary>Наибольшее число модов в приветствии.</summary>
    private const int MaxMods = 1000;

    /// <summary>Наибольшая длина строки в байтах: id, версия, хеш мода.</summary>
    private const int MaxText = 256;

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
        OwnInventory,
        ActionStarted,
        ActionEnded,
        OwnEntity,
        Attack,
    }

    /// <summary>Типы сообщений клиента.</summary>
    private enum ClientType : byte
    {
        Input = 1,
        StartAction,
        CancelAction,
        Hello,
        Control,
    }

    /// <summary>Пакет сервера в байты.</summary>
    public static byte[] Write(ServerFrame frame)
    {
        PacketWriter writer = new();
        writer.WriteVarInt(frame.Tick);
        writer.WriteVarUInt(frame.LastInput);
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
        uint lastInput = ReadSequence(reader);
        int count = reader.ReadCount(MaxMessages);
        List<ServerMessage> messages = new(count);
        for (int i = 0; i < count; i++)
        {
            messages.Add(ReadMessage(reader));
        }

        RequireEnd(reader);
        return new ServerFrame(tick, messages, lastInput);
    }

    /// <summary>Сообщение клиента в байты: тип одним байтом, дальше его поля.</summary>
    public static byte[] Write(ClientMessage message)
    {
        PacketWriter writer = new();
        switch (message)
        {
            case InputMessage input:
                writer.WriteByte((byte)ClientType.Input);
                writer.WriteVarUInt(input.Sequence);
                writer.WriteFloat(input.X);
                writer.WriteFloat(input.Y);
                writer.WriteFloat(input.AimX);
                writer.WriteFloat(input.AimY);
                break;
            case StartActionMessage start:
                writer.WriteByte((byte)ClientType.StartAction);
                writer.WriteVarInt(start.ItemId);
                writer.WriteVarUInt((ulong)start.Action);
                break;
            case CancelActionMessage:
                writer.WriteByte((byte)ClientType.CancelAction);
                break;
            case ControlMessage control:
                writer.WriteByte((byte)ClientType.Control);
                writer.WriteVarUInt((ulong)control.Control);
                writer.WriteBool(control.Pressed);
                break;
            case HelloMessage hello:
                writer.WriteByte((byte)ClientType.Hello);
                writer.WriteString(hello.PlayerId);
                break;
            default:
                throw new ArgumentException($"unknown client message {message.GetType().Name}", nameof(message));
        }

        return writer.ToArray();
    }

    /// <summary>Сообщение клиента из байт.</summary>
    public static ClientMessage ReadClientMessage(byte[] data)
    {
        PacketReader reader = new(data);
        byte type = reader.ReadByte();
        ClientMessage message = (ClientType)type switch
        {
            ClientType.Input => ReadInput(reader),
            ClientType.StartAction => new StartActionMessage(reader.ReadVarInt(), reader.ReadCount(MaxList)),
            ClientType.CancelAction => new CancelActionMessage(),
            ClientType.Hello => new HelloMessage(reader.ReadString(MaxText)),
            ClientType.Control => new ControlMessage(reader.ReadCount(MaxList), reader.ReadBool()),
            _ => throw new ProtocolException($"unknown client message type {type}"),
        };
        RequireEnd(reader);
        return message;
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
                writer.WriteDouble(welcome.TimeScale);
                writer.WriteVarUInt((ulong)welcome.Mods.Count);
                foreach (ModInfo mod in welcome.Mods)
                {
                    writer.WriteString(mod.Id);
                    writer.WriteString(mod.Version);
                    writer.WriteString(mod.Hash);
                }
                break;
            case ChunkSnapshotMessage snapshot:
                writer.WriteByte((byte)ServerType.ChunkSnapshot);
                writer.WriteVarUInt((ulong)snapshot.ChunkX);
                writer.WriteVarUInt((ulong)snapshot.ChunkY);
                writer.WriteVarUInt((ulong)snapshot.Levels.Count);
                foreach (ChunkLevelData level in snapshot.Levels)
                {
                    ChunkCodec.Write(writer, level);
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
                writer.WriteVarUInt((ulong)(spawn.Creature + 1));
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
            case OwnInventoryMessage inventory:
                writer.WriteByte((byte)ServerType.OwnInventory);
                writer.WriteVarUInt((ulong)inventory.Items.Count);
                foreach (ItemData item in inventory.Items)
                {
                    writer.WriteVarInt(item.Id);
                    writer.WriteVarUInt((ulong)item.Item);
                }

                break;
            case ActionStartedMessage started:
                writer.WriteByte((byte)ServerType.ActionStarted);
                writer.WriteVarInt(started.Entity);
                writer.WriteVarUInt((ulong)started.Action);
                writer.WriteVarInt(started.StartTick);
                writer.WriteVarUInt((ulong)started.DurationTicks);
                break;
            case AttackMessage attack:
                writer.WriteByte((byte)ServerType.Attack);
                writer.WriteVarInt(attack.Attacker);
                writer.WriteVarInt(attack.Target);
                writer.WriteByte((byte)attack.Kind);
                break;
            case OwnEntityMessage own:
                writer.WriteByte((byte)ServerType.OwnEntity);
                writer.WriteVarInt(own.EntityId);
                break;
            case ActionEndedMessage ended:
                writer.WriteByte((byte)ServerType.ActionEnded);
                writer.WriteVarInt(ended.Entity);
                writer.WriteBool(ended.Completed);
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
            ServerType.Welcome => ReadWelcome(reader),
            ServerType.ChunkSnapshot => ReadSnapshot(reader),
            ServerType.ChunkUnload => new ChunkUnloadMessage(reader.ReadCount(MaxList), reader.ReadCount(MaxList)),
            ServerType.EntitySpawn => ReadSpawn(reader),
            ServerType.EntityMove => new EntityMoveMessage(reader.ReadVarInt(), reader.ReadFloat(), reader.ReadFloat(), (int)reader.ReadVarInt()),
            ServerType.MoodleLevel => new MoodleLevelMessage(reader.ReadVarInt(), reader.ReadCount(MaxList), reader.ReadCount(MaxList)),
            ServerType.EntityRemove => new EntityRemoveMessage(reader.ReadVarInt()),
            ServerType.OwnStats => ReadOwnStats(reader),
            ServerType.FloorChange => new FloorChangeMessage(ReadCell(reader), ReadUShort(reader)),
            ServerType.EdgeChange => new EdgeChangeMessage(ReadCell(reader), reader.ReadBool(), new Edge(ReadUShort(reader), reader.ReadBool())),
            ServerType.OwnInventory => ReadInventory(reader),
            ServerType.ActionStarted => new ActionStartedMessage(reader.ReadVarInt(), reader.ReadCount(MaxList), reader.ReadVarInt(), reader.ReadCount(int.MaxValue)),
            ServerType.ActionEnded => new ActionEndedMessage(reader.ReadVarInt(), reader.ReadBool()),
            ServerType.OwnEntity => new OwnEntityMessage(reader.ReadVarInt()),
            ServerType.Attack => new AttackMessage(reader.ReadVarInt(), reader.ReadVarInt(), ReadAttackKind(reader)),
            _ => throw new ProtocolException($"unknown server message type {type}"),
        };
    }

    /// <summary>Вид удара из байта; неизвестный - ProtocolException.</summary>
    private static AttackKind ReadAttackKind(PacketReader reader)
    {
        byte kind = reader.ReadByte();
        return Enum.IsDefined((AttackKind)kind) ? (AttackKind)kind : throw new ProtocolException($"attack kind {kind} is unknown");
    }

    /// <summary>Приветствие; ускорение времени должно быть конечным и больше нуля.</summary>
    private static WelcomeMessage ReadWelcome(PacketReader reader)
    {
        long entityId = reader.ReadVarInt();
        int width = reader.ReadCount(World.MaxSize);
        int height = reader.ReadCount(World.MaxSize);
        double timeScale = reader.ReadDouble();
        if (!double.IsFinite(timeScale) || timeScale <= 0)
        {
            throw new ProtocolException($"time scale {timeScale} is not positive");
        }

        int count = reader.ReadCount(MaxMods);
        List<ModInfo> mods = new(count);
        for (int i = 0; i < count; i++)
        {
            mods.Add(new ModInfo(reader.ReadString(MaxText), reader.ReadString(MaxText), reader.ReadString(MaxText)));
        }

        return new WelcomeMessage(entityId, width, height, timeScale, mods);
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
            levels.Add(ChunkCodec.Read(reader));
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
        int creature = reader.ReadCount(MaxList) - 1;
        int count = reader.ReadCount(MaxList);
        int[] levels = new int[count];
        for (int i = 0; i < count; i++)
        {
            levels[i] = reader.ReadCount(MaxList);
        }

        return new EntitySpawnMessage(id, x, y, z, creature, levels);
    }

    /// <summary>Ввод движения после байта типа.</summary>
    private static InputMessage ReadInput(PacketReader reader) => new(ReadSequence(reader), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

    /// <summary>Номер ввода, который должен поместиться в uint.</summary>
    private static uint ReadSequence(PacketReader reader)
    {
        ulong sequence = reader.ReadVarUInt();
        return sequence <= uint.MaxValue ? (uint)sequence : throw new ProtocolException("input sequence is too large");
    }

    /// <summary>Свой инвентарь.</summary>
    private static OwnInventoryMessage ReadInventory(PacketReader reader)
    {
        int count = reader.ReadCount(MaxList);
        ItemData[] items = new ItemData[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = new ItemData(reader.ReadVarInt(), reader.ReadCount(MaxList));
        }

        return new OwnInventoryMessage(items);
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
