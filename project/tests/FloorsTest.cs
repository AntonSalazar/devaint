using System.Linq;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Тесты этажей в клиенте: узлы этажей чанка, этажи выше своего прячутся, высота на лестнице, плита лестницы, срез и затемнение своего этажа.</summary>
public class FloorsTest : TestCase
{
    /// <summary>Допуск координат.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>У стартовой комнаты два этажа узлами; этажи выше границы скрыты, без границы видны все.</summary>
    public void TestLevelsAreHidden()
    {
        GameFixture game = new();
        WorldView view = View(game);
        ChunkView chunk = view.Chunk(0, 0)!;

        view.ShowUpTo(0);
        bool upperHidden = !chunk.Level(1)!.Visible && chunk.Level(0)!.Visible;
        view.ShowUpTo(null);

        CheckEqual("0 1", string.Join(" ", chunk.Levels.Select(level => level.Z)), "two levels");
        Check(upperHidden, "upper level is hidden from the ground floor");
        Check(chunk.Level(1)!.Visible, "every level is shown without a limit");
        view.Free();
    }

    /// <summary>Этаж выше не висит в воздухе: стены первого этажа доходят до его пола, а сам пол виден и снизу, как потолок.</summary>
    public void TestUpperFloorRestsOnWalls()
    {
        GameFixture game = new();
        WorldView view = View(game);
        LevelView ground = view.Chunk(0, 0)!.Level(0)!;
        WallInstance wall = ground.Walls.First(piece => !piece.IsDoor);
        float top = wall.Transform.Origin.Y + (wall.Transform.Basis.Y.Length() / 2);
        MeshInstance3D floors = view.Chunk(0, 0)!.Level(1)!.GetNode<MeshInstance3D>("Floors");

        CheckNear(ViewMath.FloorHeight, top, Tolerance, "wall top reaches the floor above");
        CheckEqual(BaseMaterial3D.CullModeEnum.Disabled, ((BaseMaterial3D)floors.MaterialOverride).CullMode, "floor is seen from below as a ceiling");
        view.Free();
    }

    /// <summary>На лестнице капсула выше пола на долю подъёма, наверху - ровно на высоте этажа.</summary>
    public void TestPlaceOnStairs()
    {
        GameFixture game = new();
        WorldView view = View(game);

        CheckNear(0.5, view.Place(new WorldPos(4.5, 7.5, 0)).Y, Tolerance, "a sixth of the way up");
        CheckNear(ViewMath.FloorHeight, view.Place(new WorldPos(4.5, 4.5, 1)).Y, Tolerance, "upper floor");
        CheckEqual(1, ViewMath.Level(view.Place(new WorldPos(4.5, 4.5, 1)).Y), "level of the upper floor");
        CheckEqual(0, ViewMath.Level(view.Place(new WorldPos(4.5, 5.1, 0)).Y), "still the ground floor near the top of the stairs");
        view.Free();
    }

    /// <summary>Лестница - наклонная плита над своими клетками: середина на половине этажа, поднимается к северу; срез её не трогает.</summary>
    public void TestStairsSlab()
    {
        GameFixture game = new();

        ObjectInstance stairs = ChunkMesh.BuildObjects(game.Sim.World, game.Sim.WorldTable, 0, 0, 0).Single(obj => obj.IsStairs);
        Vector3 along = stairs.Transform.Basis.Z.Normalized();

        CheckNear(0, stairs.Transform.Origin.DistanceTo(new Vector3(4.5f, 1.5f, 6.5f)), Tolerance, "centre");
        Check(along.Y > 0.6f && along.Z < -0.6f, $"rises to the north: {along}");
        Check(CutawayPlan.ComputeObjects(new Vector3(4.5f, 0, 4.5f), Vector3.One.Normalized(), [stairs]).Count == 0, "stairs are not cut");
    }

    /// <summary>Наверху срезаются стены второго этажа, а стены первого остаются целыми.</summary>
    public void TestCutawayOnOwnLevel()
    {
        GameFixture game = new();
        WorldView view = View(game);
        ChunkView chunk = view.Chunk(0, 0)!;
        IsoCamera camera = new();
        camera.Init(Vector3.Zero);

        view.UpdateCutaway(new Vector3(6.5f, ViewMath.FloorHeight, 6.5f), camera.Basis.Z);
        int lower = Enumerable.Range(0, chunk.Level(0)!.Walls.Count).Count(chunk.Level(0)!.IsCut);
        int upper = Enumerable.Range(0, chunk.Level(1)!.Walls.Count).Count(chunk.Level(1)!.IsCut);

        CheckEqual(0, lower, "ground floor walls stay whole");
        Check(upper > 0, $"upper room walls towards the camera are cut: {upper}");
        camera.Free();
        view.Free();
    }

    /// <summary>Внутри комнаты у опущенной восточной стены с окном прячется и стекло: оно не висит в воздухе над пеньком.</summary>
    public void TestCutWindowHidesGlass()
    {
        GameFixture game = new();
        WorldView view = View(game);
        LevelView ground = view.Chunk(0, 0)!.Level(0)!;
        IsoCamera camera = new();
        camera.Init(Vector3.Zero);

        view.UpdateCutaway(new Vector3(7.5f, 0, 6.5f), camera.Basis.Z);
        bool hidden = ground.IsGlassCut(0);
        view.UpdateCutaway(null, camera.Basis.Z);

        CheckEqual(1, ground.Glass.Count, "one window on the ground floor");
        Check(hidden, "glass of the cut window is hidden");
        Check(!ground.IsGlassCut(0), "glass is back without the cut");
        camera.Free();
        view.Free();
    }

    /// <summary>Затемнение считает своим этаж глаз: наверху граница этажа на 3 м выше.</summary>
    public void TestOverlayFloorBand()
    {
        VisibilityOverlay overlay = new();
        overlay.Init();

        overlay.Look(new Vector3(5, ViewMath.FloorHeight + 0.2f, 5), Vector3.Back, new VisionDef { Angle = 90, Range = 10 });

        CheckNear(ViewMath.FloorHeight - 0.05, overlay.Parameter("floor_bottom").AsSingle(), Tolerance, "bottom of the upper floor");
        CheckNear((2 * ViewMath.FloorHeight) - 0.05, overlay.Parameter("floor_top").AsSingle(), Tolerance, "top of the upper floor");
        overlay.Free();
    }

    /// <summary>Этажи выше своего прячет только изометрия.</summary>
    public void TestOnlyIsoHidesUpperFloors()
    {
        CameraRig rig = new();
        rig.Init();
        bool iso = rig.Camera.HidesUpperFloors;
        rig.Next(Vector3.Zero, Vector3.Back);

        Check(iso, "iso hides upper floors");
        Check(!rig.Camera.HidesUpperFloors, "shoulder shows them: the ceiling is above");
        rig.Free();
    }

    /// <summary>Вид мира после первого пакета сервера.</summary>
    private static WorldView View(GameFixture game)
    {
        WorldView view = new();
        view.Init(game.Client.Replica, game.Sim.WorldTable, game.Sim.CreatureTable);
        game.Tick();
        return view;
    }
}
