using System;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Mods;
using DevAInt.Sim.Motion;
using DevAInt.Sim.Stats;
using DevAInt.Sim.Tests.Mods;
using Xunit;

namespace DevAInt.Sim.Tests.Motion;

/// <summary>Тесты <see cref="Movement"/> в комнате core:starter_room: стены x 4..10, y 4..9, дверь на y 9 между x 6 и 7, двор 2..12.</summary>
public class MovementTest
{
    /// <summary>Допуск сравнения координат.</summary>
    private const int Precision = 6;

    /// <summary>Радиус тела по умолчанию.</summary>
    private static readonly double _radius = MoveParams.Default.Radius;

    /// <summary>Загруженный core.</summary>
    private static readonly ModLoadResult _core = LoadCore();

    /// <summary>Идёт со скоростью ходьбы.</summary>
    [Fact]
    public void MovesAtWalkingSpeed()
    {
        WorldPos end = Walk(Room(), new WorldPos(5.5, 6.5, 0), new MoveInput(1, 0), seconds: 1);

        Assert.Equal(7.0, end.X, Precision);
        Assert.Equal(6.5, end.Y, Precision);
    }

    /// <summary>По диагонали не быстрее, чем прямо.</summary>
    [Fact]
    public void DiagonalIsNotFaster()
    {
        WorldPos end = Walk(Room(), new WorldPos(5.5, 5.5, 0), new MoveInput(1, 1), seconds: 1);

        Assert.Equal(1.5, Math.Sqrt(Math.Pow(end.X - 5.5, 2) + Math.Pow(end.Y - 5.5, 2)), Precision);
    }

    /// <summary>Упирается в стену и останавливается на радиус от неё.</summary>
    [Fact]
    public void StopsAtWall()
    {
        WorldPos end = Walk(Room(), new WorldPos(5.5, 6.5, 0), new MoveInput(-1, 0), seconds: 5);

        Assert.Equal(4 + _radius, end.X, Precision);
        Assert.Equal(6.5, end.Y, Precision);
    }

    /// <summary>Итог этапа 5: при движении в стену под углом тело скользит вдоль неё.</summary>
    [Fact]
    public void SlidesAlongWall()
    {
        WorldPos end = Walk(Room(), new WorldPos(4.5, 5.5, 0), new MoveInput(-1, 1), seconds: 2);

        Assert.Equal(4 + _radius, end.X, Precision);
        Assert.True(end.Y > 7, $"slid only to y {end.Y}");
    }

    /// <summary>Угол комнаты держит по обеим осям.</summary>
    [Fact]
    public void CornerHoldsBothAxes()
    {
        WorldPos end = Walk(Room(), new WorldPos(5.5, 5.5, 0), new MoveInput(-1, -1), seconds: 5);

        Assert.Equal(4 + _radius, end.X, Precision);
        Assert.Equal(4 + _radius, end.Y, Precision);
    }

    /// <summary>Итог этапа 5: закрытая дверь не пропускает.</summary>
    [Fact]
    public void ClosedDoorBlocks()
    {
        WorldPos end = Walk(Room(), new WorldPos(6.5, 8.0, 0), new MoveInput(0, 1), seconds: 3);

        Assert.Equal(9 - _radius, end.Y, Precision);
    }

    /// <summary>Итог этапа 5: открытая дверь пропускает во двор, край двора держит.</summary>
    [Fact]
    public void OpenDoorLetsThrough()
    {
        WorldPos end = Walk(Room(doorOpen: true), new WorldPos(6.5, 8.0, 0), new MoveInput(0, 1), seconds: 3);

        Assert.Equal(12 - _radius, end.Y, Precision);
        Assert.Equal(6.5, end.X, Precision);
    }

    /// <summary>Подойдя к двери не по центру, тело обтекает угол проёма и проходит.</summary>
    [Fact]
    public void SlipsIntoDoorwayOffCenter()
    {
        World world = Room(doorOpen: true);
        WorldPos position = new(6.15, 8.0, 0);

        // На каждом тике тело не ближе радиуса к углам проёма, то есть не залезает в косяк
        for (int i = 0; i < 20; i++)
        {
            position = Movement.Step(world, position, new MoveInput(0, 1), GameClock.RealSecondsPerTick, MoveParams.Default);
            Assert.True(Distance(position, 6, 9) >= _radius - 1e-9, $"inside the west door frame at {position}");
            Assert.True(Distance(position, 7, 9) >= _radius - 1e-9, $"inside the east door frame at {position}");
        }

        Assert.True(position.Y > 10, $"stuck at {position}");
    }

    /// <summary>На огромной скорости тело не проходит сквозь стену.</summary>
    [Fact]
    public void NoTunnelingAtHighSpeed()
    {
        MoveParams fast = MoveParams.Default with { Speed = 100 };

        WorldPos end = Movement.Step(Room(), new WorldPos(5.5, 6.5, 0), new MoveInput(1, 0), GameClock.RealSecondsPerTick, fast);

        Assert.Equal(10 - _radius, end.X, Precision);
    }

    /// <summary>Край пола держит как стена.</summary>
    [Fact]
    public void FloorEdgeIsWall()
    {
        WorldPos end = Walk(Room(), new WorldPos(5.5, 3.0, 0), new MoveInput(0, -1), seconds: 5);

        Assert.Equal(2 + _radius, end.Y, Precision);
    }

    /// <summary>Без ввода позиция не меняется.</summary>
    [Fact]
    public void NoInputStaysPut()
    {
        WorldPos start = new(5.5, 6.5, 0);

        Assert.Equal(start, Movement.Step(Room(), start, MoveInput.None, GameClock.RealSecondsPerTick, MoveParams.Default));
    }

    /// <summary>Одинаковые данные дают одинаковый результат, мир не меняется.</summary>
    [Fact]
    public void StepIsPureAndDeterministic()
    {
        World world = Room();
        WorldPos start = new(4.5, 5.5, 0);
        Edge door = world.GetEdge(new CellPos(6, 8, 0), Side.South);

        WorldPos first = Walk(world, start, new MoveInput(-0.3, 0.9), seconds: 3);
        WorldPos second = Walk(world, start, new MoveInput(-0.3, 0.9), seconds: 3);

        Assert.Equal(first, second);
        Assert.Equal(door, world.GetEdge(new CellPos(6, 8, 0), Side.South));
    }

    /// <summary>Расстояние от позиции до точки.</summary>
    private static double Distance(WorldPos position, double x, double y) => Math.Sqrt(Math.Pow(position.X - x, 2) + Math.Pow(position.Y - y, 2));

    /// <summary>Тики по 0.1 с, как на сервере.</summary>
    private static WorldPos Walk(World world, WorldPos from, MoveInput input, double seconds)
    {
        WorldPos position = from;
        int ticks = (int)Math.Round(seconds / GameClock.RealSecondsPerTick);
        for (int i = 0; i < ticks; i++)
        {
            position = Movement.Step(world, position, input, GameClock.RealSecondsPerTick, MoveParams.Default);
        }

        return position;
    }

    /// <summary>Новый мир стартовой комнаты, дверь по желанию открыта.</summary>
    private static World Room(bool doorOpen = false)
    {
        WorldTable table = new(_core.Defs);
        World world = MapBuilder.Build(_core.Defs.Get<MapDef>("core:starter_room"), table);
        CellPos doorCell = new(6, 8, 0);
        world.SetEdge(doorCell, Side.South, world.GetEdge(doorCell, Side.South) with { Open = doorOpen });
        return world;
    }

    /// <summary>Загрузка core из папки mods репозитория.</summary>
    private static ModLoadResult LoadCore()
    {
        ModLoadResult result = ModLoader.Load(RepoMods.Root, ["core"]);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        return result;
    }
}
