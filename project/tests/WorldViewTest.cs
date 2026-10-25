using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using Godot;
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
        _view.Init(_game.Client.Replica, _game.Sim.WorldTable, _game.Sim.CreatureTable);
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

    /// <summary>Своя капсула идёт по предсказанию клиента через интерполяцию.</summary>
    public void TestInputMovesCapsule()
    {
        _game.Tick();
        long own = _game.Client.Replica.OwnEntityId;

        _game.Client.SendInput(new MoveInput(1, 0));
        _view.MoveOwn(_game.Client.OwnPosition!.Value);
        _view.Advance(1);

        CheckNear(6.5 + 0.15, _view.Entity(own)!.Position.X, Tolerance, "moved one tick east before the server answered");
    }

    /// <summary>Позиции своей сущности от сервера капсулу не двигают, чужие сущности двигают.</summary>
    public void TestServerMovesOnlyOthers()
    {
        long npc = _game.Sim.CreateEntity(new WorldPos(3.5, 3.5, 0));
        _game.Tick();
        long own = _game.Client.Replica.OwnEntityId;

        // Сервер сам ведёт обе сущности на восток, клиент ввода не шлёт
        _game.Sim.Entity(own).Input = new MoveInput(1, 0);
        _game.Sim.Entity(npc).Input = new MoveInput(1, 0);
        _game.Tick();
        _view.Advance(1);

        CheckNear(6.5, _view.Entity(own)!.Position.X, Tolerance, "own capsule waits for prediction");
        CheckNear(3.5 + 0.15, _view.Entity(npc)!.Position.X, Tolerance, "npc follows the server");
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

    /// <summary>Итог этапа 9: во время еды над капсулой растёт полоса прогресса, после еды она пропадает.</summary>
    public void TestActionBarFollowsEating()
    {
        _game.Tick();
        long own = _game.Client.Replica.OwnEntityId;
        _game.Client.SendStartAction(_game.Client.Replica.Inventory[0].Id, _game.Sim.ItemTable.ActionIndex("core:eat"));

        _game.Tick(10);
        _view.Advance(0);
        float early = _view.Entity(own)!.ActionProgress ?? -1;
        _game.Tick(10);
        _view.Advance(0, tickFraction: 0.5);
        float later = _view.Entity(own)!.ActionProgress ?? -1;
        _game.Tick(25);
        _view.Advance(0);

        Check(early > 0 && later > early, $"progress grows: {early} then {later}");
        Check(!_view.Entity(own)!.ActionBarVisible, "bar is hidden after eating");
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

    /// <summary>Цвет капсулы из вида существа, своя - с кольцом.</summary>
    public void TestCapsuleColorFromCreature()
    {
        _game.Tick();
        EntityView own = _view.Entity(_game.Client.Replica.OwnEntityId)!;
        string human = _game.Sim.CreatureTable.Creature(_game.Sim.CreatureTable.Index("core:human")).Color;

        CheckEqual(Color.FromHtml(human), own.BodyColor, "human color");
        Check(own.HasRing, "own ring");
    }

    /// <summary>Удар по сети: свой выпад и вспышка цели; взгляд своей капсулы по вводу.</summary>
    public void TestAttackShowsLungeAndFlash()
    {
        _game.Tick();
        long dummy = _game.Sim.CreateEntity(new WorldPos(6.5, 7.3, 0), "core:human");
        _game.Tick();
        _view.FaceOwn(new MoveInput(0, 1));

        _game.Client.SendControl(_game.Sim.ControlTable.Index("core:aim"), true);
        _game.Client.SendInput(MoveInput.None, new WorldPos(6.5, 7.3, 0));
        _game.Client.SendControl(_game.Sim.ControlTable.Index("core:attack"), true);
        _game.Tick();
        _view.Advance(EntityView.LungeSeconds / 2);

        EntityView own = _view.Entity(_game.Client.Replica.OwnEntityId)!;
        Check(own.LungeOffset > 0.1f, $"own lunges: {own.LungeOffset}");
        Check(_view.Entity(dummy)!.IsFlashing, "target flashes");
        CheckNear(1, own.Facing.Z, Tolerance, "own faces south");
    }

    /// <summary>Толчок по сети: свой выпад, но цель не вспыхивает, урона нет.</summary>
    public void TestShoveLungesWithoutFlash()
    {
        _game.Tick();
        long dummy = _game.Sim.CreateEntity(new WorldPos(6.5, 7.3, 0), "core:human");
        _game.Tick();

        _game.Client.SendControl(_game.Sim.ControlTable.Index("core:shove"), true);
        _game.Tick();
        _view.Advance(EntityView.LungeSeconds / 2);

        Check(_view.Entity(_game.Client.Replica.OwnEntityId)!.LungeOffset > 0.1f, "own lunges");
        Check(!_view.Entity(dummy)!.IsFlashing, "shoved target does not flash");
    }
}
