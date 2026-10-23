using System;
using System.Buffers.Binary;

namespace DevAInt.Sim.Net;

/// <summary>Ошибка разбора пакета: обрезан, неизвестный тип или неверные данные.</summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Чтение пакета, записанного PacketWriter; на неверных данных - ProtocolException.</summary>
public sealed class PacketReader(byte[] data)
{
    /// <summary>Наибольшая длина числа переменной длины в байтах.</summary>
    private const int MaxVarIntBytes = 10;

    /// <summary>Позиция чтения.</summary>
    private int _position;

    /// <summary>Прочитан ли пакет до конца.</summary>
    public bool AtEnd => _position == data.Length;

    /// <summary>Один байт.</summary>
    public byte ReadByte()
    {
        if (_position >= data.Length)
        {
            throw new ProtocolException("packet is truncated");
        }

        return data[_position++];
    }

    /// <summary>Логическое значение, только 0 или 1.</summary>
    public bool ReadBool()
    {
        byte value = ReadByte();
        return value <= 1 ? value == 1 : throw new ProtocolException($"bool byte {value} must be 0 or 1");
    }

    /// <summary>Неотрицательное число переменной длины.</summary>
    public ulong ReadVarUInt()
    {
        ulong value = 0;
        for (int i = 0; i < MaxVarIntBytes; i++)
        {
            byte part = ReadByte();
            value |= (ulong)(part & 0x7F) << (7 * i);
            if ((part & 0x80) == 0)
            {
                return value;
            }
        }

        throw new ProtocolException("varint is too long");
    }

    /// <summary>Знаковое число переменной длины.</summary>
    public long ReadVarInt()
    {
        ulong raw = ReadVarUInt();
        return (long)(raw >> 1) ^ -(long)(raw & 1);
    }

    /// <summary>Неотрицательное число, которое должно поместиться в int не больше max.</summary>
    public int ReadCount(int max)
    {
        ulong value = ReadVarUInt();
        return value <= (ulong)max ? (int)value : throw new ProtocolException($"count {value} is larger than {max}");
    }

    /// <summary>Float из 4 байт little-endian.</summary>
    public float ReadFloat()
    {
        if (_position + 4 > data.Length)
        {
            throw new ProtocolException("packet is truncated");
        }

        float value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(_position));
        _position += 4;
        return value;
    }

    /// <summary>Массив из серий; сумма длин серий должна совпасть с длиной массива.</summary>
    public void ReadRuns(Span<ushort> target)
    {
        int runs = ReadCount(target.Length);
        int filled = 0;
        for (int i = 0; i < runs; i++)
        {
            ulong value = ReadVarUInt();
            int length = ReadCount(target.Length - filled);
            if (value > ushort.MaxValue)
            {
                throw new ProtocolException($"run value {value} does not fit ushort");
            }

            target.Slice(filled, length).Fill((ushort)value);
            filled += length;
        }

        if (filled != target.Length)
        {
            throw new ProtocolException($"runs cover {filled} of {target.Length} values");
        }
    }
}
