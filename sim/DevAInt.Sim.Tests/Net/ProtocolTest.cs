using System.Collections.Generic;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Net;
using Xunit;

namespace DevAInt.Sim.Tests.Net;

/// <summary>Тесты <see cref="Protocol"/>: каждое сообщение туда и обратно, защита от мусора.</summary>
public class ProtocolTest
{
    /// <summary>Все сообщения без списков читаются равными записанным.</summary>
    [Fact]
    public void SimpleMessagesRoundTrip()
    {
        List<ServerMessage> messages =
        [
            new WelcomeMessage(7, 4096, 32, 2.5, [new ModInfo("core", "0.1.0", new string('a', 64)), new ModInfo("zombies", "1.2.3", "ff")]),
            new ChunkUnloadMessage(3, 127),
            new EntityMoveMessage(300, 12.5f, -0.25f, -2),
            new MoodleLevelMessage(5, 2, 3),
            new EntityRemoveMessage(long.MaxValue),
            new FloorChangeMessage(new CellPos(4095, 0, 8), 65_535),
            new EdgeChangeMessage(new CellPos(32, 31, 0), true, new Edge(9, true)),
            new AttackMessage(3, long.MaxValue),
            new AttackMessage(4, 5, Sim.Creatures.AttackKind.Shove),
            new OwnEntityMessage(12),
            new ObjectPlaceMessage(new ObjectData(long.MaxValue, 65_535, new CellPos(4095, 4095, 8), 1)),
            new ObjectRemoveMessage(42),
            new ContainerMessage(7, [new ItemData(1, 0), new ItemData(long.MaxValue, 3)]),
            new ContainerCloseMessage(7),
        ];

        ServerFrame read = Protocol.ReadServerFrame(Protocol.Write(new ServerFrame(123_456, messages, uint.MaxValue)));

        Assert.Equal(123_456, read.Tick);
        Assert.Equal(uint.MaxValue, read.LastInput);
        Assert.Equal(messages, read.Messages);
    }

    /// <summary>Сообщения со списками читаются с теми же значениями.</summary>
    [Fact]
    public void ListMessagesRoundTrip()
    {
        ushort[] floors = new ushort[1024];
        floors[5] = 2;
        ChunkLevelData level = new(-1, floors, new ushort[1024], new ushort[1024], new ushort[1024]);
        level.Flags[1023] = 3;
        ServerFrame frame = new(1,
        [
            new ChunkSnapshotMessage(1, 2, [level], [new ObjectData(7, 3, new CellPos(40, 70, -1), 3)]),
            new EntitySpawnMessage(9, 1.5f, 2.5f, 0, 2, [0, 3, 1]),
            new EntitySpawnMessage(10, 0.5f, 0.5f, 1, -1, []),
            new OwnStatsMessage([0f, 41.5f, 100f]),
        ]);

        ServerFrame read = Protocol.ReadServerFrame(Protocol.Write(frame));

        ChunkSnapshotMessage snapshot = Assert.IsType<ChunkSnapshotMessage>(read.Messages[0]);
        ChunkLevelData readLevel = Assert.Single(snapshot.Levels);
        Assert.Equal((1, 2, -1), (snapshot.ChunkX, snapshot.ChunkY, readLevel.Z));
        Assert.Equal(floors, readLevel.Floors);
        Assert.Equal(3, readLevel.Flags[1023]);
        Assert.Equal(new ObjectData(7, 3, new CellPos(40, 70, -1), 3), Assert.Single(snapshot.Objects));

        EntitySpawnMessage spawn = Assert.IsType<EntitySpawnMessage>(read.Messages[1]);
        Assert.Equal((9L, 1.5f, 2.5f, 0, 2), (spawn.Id, spawn.X, spawn.Y, spawn.Z, spawn.Creature));
        Assert.Equal([0, 3, 1], spawn.MoodleLevels);
        Assert.Equal(-1, Assert.IsType<EntitySpawnMessage>(read.Messages[2]).Creature);

        Assert.Equal([0f, 41.5f, 100f], Assert.IsType<OwnStatsMessage>(read.Messages[3]).Values);
    }

    /// <summary>Каждое сообщение клиента туда и обратно.</summary>
    [Fact]
    public void ClientMessagesRoundTrip()
    {
        ClientMessage[] messages = [new InputMessage(uint.MaxValue, -0.5f, 1f), new StartActionMessage(123_456_789, 3), new CancelActionMessage(), new ControlMessage(7, true), new InputMessage(3, 1, 0, 12.5f, -4f), new InputMessage(4, 0, 1, 2f, 3f, FaceAim: true), new WatchContainersMessage([3, 9]), new WatchContainersMessage([]), new MoveItemMessage(5, 0, 12), new StartCraftMessage(3), new BuildMessage(new BuildTarget(BuildKind.Wall, 2, new CellPos(5, 6, 0), 3)), new HelloMessage("anna")];

        foreach (ClientMessage message in messages)
        {
            Assert.Equal(message, Protocol.ReadClientMessage(Protocol.Write(message)));
        }
    }

    /// <summary>Инвентарь и действия туда и обратно.</summary>
    [Fact]
    public void InventoryAndActionMessagesRoundTrip()
    {
        ServerFrame frame = new(7,
        [
            new OwnInventoryMessage([new ItemData(1, 0), new ItemData(500_000, 2)]),
            new ActionStartedMessage(4, 1, 1_000_000, 40),
            new ActionStartedMessage(5, 2, 7, 80, "core:build", "core:crate"),
            new ActionEndedMessage(4, true),
        ]);

        ServerFrame read = Protocol.ReadServerFrame(Protocol.Write(frame));

        Assert.Equal([new ItemData(1, 0), new ItemData(500_000, 2)], Assert.IsType<OwnInventoryMessage>(read.Messages[0]).Items);
        Assert.Equal(frame.Messages[1], read.Messages[1]);
        Assert.Equal(frame.Messages[2], read.Messages[2]);
    }

    /// <summary>Неизвестный тип, лишние байты и обрезанный пакет - ProtocolException.</summary>
    [Fact]
    public void GarbageThrowsProtocolException()
    {
        byte[] valid = Protocol.Write(new ServerFrame(1, [new EntityRemoveMessage(1)]));

        Assert.Throws<ProtocolException>(() => Protocol.ReadServerFrame([2, 1, 99]));
        Assert.Throws<ProtocolException>(() => Protocol.ReadServerFrame([.. valid, 0]));
        Assert.Throws<ProtocolException>(() => Protocol.ReadServerFrame(valid[..^1]));
        Assert.Throws<ProtocolException>(() => Protocol.ReadClientMessage([1, 2]));
        Assert.Throws<ProtocolException>(() => Protocol.ReadClientMessage([99]));
        Assert.Throws<ProtocolException>(() => Protocol.ReadClientMessage([3, 0]));
    }

    /// <summary>Приветствие с нулевым, отрицательным или нечисловым ускорением времени - ProtocolException.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BadTimeScaleThrowsProtocolException(double timeScale)
    {
        byte[] packet = Protocol.Write(new ServerFrame(1, [new WelcomeMessage(1, 32, 32, timeScale, [])]));

        Assert.Throws<ProtocolException>(() => Protocol.ReadServerFrame(packet));
    }
}
