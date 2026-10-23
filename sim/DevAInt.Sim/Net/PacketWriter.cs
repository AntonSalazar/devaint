using System;
using System.Buffers.Binary;

namespace DevAInt.Sim.Net;

/// <summary>Запись пакета: байты, числа переменной длины, float и серии одинаковых значений.</summary>
public sealed class PacketWriter
{
    /// <summary>Буфер, растёт по мере записи.</summary>
    private byte[] _buffer = new byte[256];

    /// <summary>Число записанных байт.</summary>
    public int Length { get; private set; }

    /// <summary>Копия записанных байт.</summary>
    public byte[] ToArray() => _buffer.AsSpan(0, Length).ToArray();

    /// <summary>Один байт.</summary>
    public void WriteByte(byte value)
    {
        Reserve(1);
        _buffer[Length++] = value;
    }

    /// <summary>Логическое значение одним байтом.</summary>
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Неотрицательное число по 7 бит в байте: до 127 - один байт.</summary>
    public void WriteVarUInt(ulong value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        WriteByte((byte)value);
    }

    /// <summary>Знаковое число: маленькие по модулю, в том числе отрицательные, занимают один байт.</summary>
    public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

    /// <summary>Float в 4 байтах little-endian.</summary>
    public void WriteFloat(float value)
    {
        Reserve(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(Length), value);
        Length += 4;
    }

    /// <summary>Массив сериями: число серий, затем пары значение и длина серии.</summary>
    public void WriteRuns(ReadOnlySpan<ushort> values)
    {
        int runs = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (i == 0 || values[i] != values[i - 1])
            {
                runs++;
            }
        }

        WriteVarUInt((ulong)runs);
        int start = 0;
        for (int i = 1; i <= values.Length; i++)
        {
            if (i == values.Length || values[i] != values[start])
            {
                WriteVarUInt(values[start]);
                WriteVarUInt((ulong)(i - start));
                start = i;
            }
        }
    }

    /// <summary>Место ещё под count байт.</summary>
    private void Reserve(int count)
    {
        if (Length + count > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + count));
        }
    }
}
