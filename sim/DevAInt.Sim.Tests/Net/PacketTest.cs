using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты <see cref="PacketWriter"/> и <see cref="PacketReader"/>.</summary>
public class PacketTest
{
    /// <summary>Беззнаковые числа на границах длины читаются как записаны и занимают ожидаемое число байт.</summary>
    [Theory]
    [InlineData(0UL, 1)]
    [InlineData(127UL, 1)]
    [InlineData(128UL, 2)]
    [InlineData(16_383UL, 2)]
    [InlineData(16_384UL, 3)]
    [InlineData(ulong.MaxValue, 10)]
    public void VarUIntRoundTrips(ulong value, int bytes)
    {
        PacketWriter writer = new();
        writer.WriteVarUInt(value);

        Assert.Equal(bytes, writer.Length);
        Assert.Equal(value, new PacketReader(writer.ToArray()).ReadVarUInt());
    }

    /// <summary>Знаковые числа, маленькие по модулю - один байт.</summary>
    [Theory]
    [InlineData(0L, 1)]
    [InlineData(-1L, 1)]
    [InlineData(63L, 1)]
    [InlineData(-64L, 1)]
    [InlineData(64L, 2)]
    [InlineData(long.MinValue, 10)]
    [InlineData(long.MaxValue, 10)]
    public void VarIntRoundTrips(long value, int bytes)
    {
        PacketWriter writer = new();
        writer.WriteVarInt(value);

        Assert.Equal(bytes, writer.Length);
        Assert.Equal(value, new PacketReader(writer.ToArray()).ReadVarInt());
    }

    /// <summary>Float и bool читаются как записаны.</summary>
    [Fact]
    public void FloatAndBoolRoundTrip()
    {
        PacketWriter writer = new();
        writer.WriteFloat(-12.375f);
        writer.WriteBool(true);
        PacketReader reader = new(writer.ToArray());

        Assert.Equal(-12.375f, reader.ReadFloat());
        Assert.True(reader.ReadBool());
        Assert.True(reader.AtEnd);
    }

    /// <summary>Серии: одинаковый массив занимает пару байт, разный читается без потерь.</summary>
    [Fact]
    public void RunsRoundTrip()
    {
        ushort[] flat = new ushort[1024];
        ushort[] mixed = new ushort[1024];
        for (int i = 0; i < mixed.Length; i++)
        {
            mixed[i] = (ushort)(i % 7 == 0 ? 65_535 : i / 100);
        }

        PacketWriter writer = new();
        writer.WriteRuns(flat);
        int flatBytes = writer.Length;
        writer.WriteRuns(mixed);
        PacketReader reader = new(writer.ToArray());
        ushort[] readFlat = new ushort[1024];
        ushort[] readMixed = new ushort[1024];
        reader.ReadRuns(readFlat);
        reader.ReadRuns(readMixed);

        Assert.True(flatBytes <= 4, $"flat array took {flatBytes} bytes");
        Assert.Equal(flat, readFlat);
        Assert.Equal(mixed, readMixed);
    }

    /// <summary>Обрезанные и неверные данные - ProtocolException, а не другое исключение.</summary>
    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })]
    public void BadVarIntThrowsProtocolException(byte[] data) => Assert.Throws<ProtocolException>(() => new PacketReader(data).ReadVarUInt());

    /// <summary>Bool только 0 или 1, float целиком, серии ровно на длину массива.</summary>
    [Fact]
    public void BadValuesThrowProtocolException()
    {
        Assert.Throws<ProtocolException>(() => new PacketReader([2]).ReadBool());
        Assert.Throws<ProtocolException>(() => new PacketReader([1, 2, 3]).ReadFloat());

        // Одна серия значения 5 длиной 3 при массиве из 4
        Assert.Throws<ProtocolException>(() => new PacketReader([1, 5, 3]).ReadRuns(new ushort[4]));
    }
}
