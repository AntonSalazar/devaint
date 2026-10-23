using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Тесты <see cref="WorldView"/> на настоящих сервере и клиенте в одном процессе.</summary>
public class WorldViewTest : TestCase
{
    /// <summary>Допуск координат.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>Сервер и клиент.</summary>
    private GameFixture _game = null!;

    /// <summary>Вид мира.</summary>
    private WorldView _view = null!;

    /// <summary>Новая игра и вид перед каждым тестом.</summary>
    public override void BeforeEach()
    {
        _game = new GameFixture();
        _view = new WorldView();
        _view.Init(_game.Client.Replica, _game.Sim.WorldTable);
    }

    /// <summary>Освобождение вида после теста.</summary>
    public override void AfterEach() => _view.Free();

    /// <summary>Моды нашлись, core загрузился без ошибок.</summary>
    public void TestCoreLoads()
    {
        Check(_game.Loaded.Ok, string.Join("; ", _game.Loaded.Errors));
        Check(ModsPath.Find().EndsWith("mods"), $"mods path {ModsPath.Find()}");
    }

    /// <summary>После подключения есть все четыре чанка хранения карты 32 на 32 и своя капсула в точке появления.</summary>
    public void TestConnectBuildsChunksAndEntity()
    {
        _game.Tick();

        long own = _game.Client.Replica.OwnEntityId;
        EntityView? view = _view.Entity(own);
        CheckEqual(4, _view.ChunkCount, "chunks");
        CheckEqual(1, _view.EntityCount, "entities");
        Check(view is not null, "own entity view exists");
        CheckNear(6.5, view!.Position.X, Tolerance, "spawn x");
        CheckNear(6.5, view.Position.Z, Tolerance, "spawn z");
        Check(_view.Chunk(0, 0)!.HasFloors, "chunk 0,0 has floors");
    }

    /// <summary>Ввод клиента двигает капсулу через сервер и интерполяцию.</summary>
    public void TestInputMovesCapsule()
    {
        _game.Tick();
        long own = _game.Client.Replica.OwnEntityId;

        _game.Client.SendInput(new MoveInput(1, 0));
        _game.Tick();
        _view.Advance(1);

        CheckNear(6.5 + 0.15, _view.Entity(own)!.Position.X, Tolerance, "moved one tick east");
    }

    /// <summary>Открытая на сервере дверь перестраивает чанк клиента.</summary>
    public void TestOpenedDoorRebuildsChunk()
    {
        _game.Tick();
        CellPos doorCell = new(6, 8, 0);
        World world = _game.Sim.World;

        world.SetEdge(doorCell, Side.South, world.GetEdge(doorCell, Side.South) with { Open = true });
        _game.Tick();

        Check(_view.Chunk(0, 0)!.Walls.Single(wall => wall.IsDoor).Open, "door is open in the view");
    }

    /// <summary>Удалённая на сервере сущность пропадает из вида.</summary>
    public void TestRemovedEntityIsDropped()
    {
        long npc = _game.Sim.CreateEntity(new WorldPos(3.5, 3.5, 0));
        _game.Tick();
        CheckEqual(2, _view.EntityCount, "entities with npc");

        _game.Sim.RemoveEntity(npc);
        _game.Tick();

        CheckEqual(1, _view.EntityCount, "entities after removal");
        Check(_view.Entity(npc) is null, "npc view is gone");
    }
}
