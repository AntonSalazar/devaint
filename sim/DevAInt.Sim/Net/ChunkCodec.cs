using System.Linq;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Net;

/// <summary>Слой этажа чанка в байтах: один код для снимка в сети и для сохранения.</summary>
internal static class ChunkCodec
{
    /// <summary>Клеток в слое чанка.</summary>
    private const int Cells = World.ChunkSize * World.ChunkSize;

    /// <summary>Копия слоя мира с номерами как в мире.</summary>
    public static ChunkLevelData Copy(int z, ChunkLevel level)
    {
        ushort[] flags = [.. level.Flags.Select(flag => (ushort)flag)];
        return new ChunkLevelData(z, [.. level.Floors], [.. level.NorthWalls], [.. level.WestWalls], flags, [.. level.NorthFills], [.. level.WestFills]);
    }

    /// <summary>Этаж и шесть массивов сериями.</summary>
    public static void Write(PacketWriter writer, ChunkLevelData level)
    {
        writer.WriteVarInt(level.Z);
        writer.WriteRuns(level.Floors);
        writer.WriteRuns(level.NorthWalls);
        writer.WriteRuns(level.WestWalls);
        writer.WriteRuns(level.Flags);
        writer.WriteRuns(level.NorthFills);
        writer.WriteRuns(level.WestFills);
    }

    /// <summary>Слой из байт; withFills - в данных есть створки, в сохранениях до формата 6 их нет. Этаж проверяет тот, кто знает мир.</summary>
    public static ChunkLevelData Read(PacketReader reader, bool withFills = true)
    {
        int z = (int)reader.ReadVarInt();
        ChunkLevelData level = new(z, new ushort[Cells], new ushort[Cells], new ushort[Cells], new ushort[Cells], new ushort[Cells], new ushort[Cells]);
        reader.ReadRuns(level.Floors);
        reader.ReadRuns(level.NorthWalls);
        reader.ReadRuns(level.WestWalls);
        reader.ReadRuns(level.Flags);
        if (withFills)
        {
            reader.ReadRuns(level.NorthFills);
            reader.ReadRuns(level.WestFills);
        }

        return level;
    }
}
