using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Тесты <see cref="Cutaway"/>: шейдер подключён к стенам и получает фокус.</summary>
public class CutawayTest : TestCase
{
    /// <summary>Материал стен использует шейдер среза.</summary>
    public void TestMaterialUsesShader()
    {
        Check(Cutaway.Material.Shader is not null, "shader is loaded");
        CheckEqual(Cutaway.ShaderPath, Cutaway.Material.Shader!.ResourcePath, "shader path");
    }

    /// <summary>Видимые стены со срезом не отбрасывают тени, тени дают те же стены целиком.</summary>
    public void TestShadowsComeFromWholeWalls()
    {
        GameFixture game = new();
        WorldView view = new();
        view.Init(game.Client.Replica, game.Sim.WorldTable, game.Sim.CreatureTable);
        game.Tick();
        ChunkView chunk = view.Chunk(0, 0)!;

        CheckEqual(GeometryInstance3D.ShadowCastingSetting.Off, chunk.VisibleWalls.CastShadow, "visible walls cast no shadows");
        CheckEqual(GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, chunk.ShadowWalls.CastShadow, "shadow walls only cast shadows");
        Check(chunk.VisibleWalls.MaterialOverride == Cutaway.Material, "visible walls use the cutaway");
        Check(chunk.ShadowWalls.MaterialOverride != Cutaway.Material, "shadow walls do not use the cutaway");
        Check(chunk.VisibleWalls.Multimesh == chunk.ShadowWalls.Multimesh, "both share one multimesh");
        view.Free();
    }

    /// <summary>Внутри комнаты опущены все её стены со стороны камеры: южная с дверью и восточная, северная и западная целые.</summary>
    public void TestInsideRoomCutsCameraWalls()
    {
        HashSet<WallKey> cut = Plan(6.5f, 6.5f);

        CheckEqual(11, cut.Count, "south and east walls");
        Check(cut.Contains(new WallKey(new CellPos(6, 9, 0), true)), "door edge");
        Check(cut.Contains(new WallKey(new CellPos(10, 4, 0), false)), "east wall");
        Check(!cut.Contains(new WallKey(new CellPos(6, 4, 0), true)), "north wall stays");
        Check(!cut.Contains(new WallKey(new CellPos(4, 6, 0), false)), "west wall stays");
    }

    /// <summary>Баг со щелью: внутри у восточной стены опущена вся стена, а не один кусок перед персонажем.</summary>
    public void TestInsideByWallHasNoSlit() => CheckEqual(11, Plan(9.6f, 6.5f).Count, "whole camera walls of the room");

    /// <summary>Снаружи за северной стеной опущен её отрезок вокруг персонажа, остальные стены целые.</summary>
    public void TestBehindBuildingCutsRun()
    {
        HashSet<WallKey> cut = Plan(6.5f, 3.4f);

        Check(cut.Count >= 3, $"run around the player: {cut.Count}");
        Check(cut.All(key => key.North && key.Owner.Y == 4), "only the north wall");
    }

    /// <summary>Шейдер читает признак среза из красного канала: у целого куска он 0, у опущенного 1.</summary>
    public void TestCutData()
    {
        CheckEqual(0f, ChunkView.CutData(false).R, "whole piece");
        CheckEqual(1f, ChunkView.CutData(true).R, "cut piece");
    }

    /// <summary>Вид мира ставит признак среза кускам стен и снимает его, когда персонажа нет.</summary>
    public void TestWorldViewAppliesCut()
    {
        GameFixture game = new();
        WorldView view = new();
        view.Init(game.Client.Replica, game.Sim.WorldTable, game.Sim.CreatureTable);
        game.Tick();
        ChunkView chunk = view.Chunk(0, 0)!;
        int door = chunk.Walls.ToList().FindIndex(wall => wall.IsDoor);

        view.UpdateCutaway(new Vector3(6.5f, 0, 6.5f), Back());
        bool cut = chunk.IsCut(door);
        view.UpdateCutaway(null, Back());

        Check(cut, "door is cut from inside");
        Check(!chunk.IsCut(door), "door is whole without a player");
        view.Free();
    }

    /// <summary>Баг: снаружи у юго-восточного угла, чуть севернее линии южной стены, стены не закрывают персонажа и не режутся.</summary>
    public void TestOutsideNearCornerCutsNothing() => CheckEqual(0, Plan(10.4f, 8.7f).Count, "walls cut outside the corner");

    /// <summary>Баг: снаружи прямо перед юго-восточным углом конец восточной стены позади тела и не режется.</summary>
    public void TestOutsideInFrontOfCornerCutsNothing() => CheckEqual(0, Plan(9.9f, 9.25f).Count, "walls cut in front of the corner");

    /// <summary>Внутри у угла кусок, закрывающий хотя бы край тела, режется.</summary>
    public void TestInsideAtCornerIsCut() => Check(Occluding(9.6f, 8.6f) > 0, "corner hides the player inside");

    /// <summary>Внутри комнаты у южной стены её кусок между персонажем и камерой режется.</summary>
    public void TestInsideBehindWallIsCut() => Check(Occluding(7.5f, 8.5f) > 0, "south wall hides the player inside");

    /// <summary>Снаружи перед стеной, на одной стороне с камерой, ничего не режется.</summary>
    public void TestOutsideInFrontCutsNothing() => CheckEqual(0, Plan(7.5f, 9.5f).Count, "walls cut in front of the wall");

    /// <summary>План среза для персонажа в точке x, y по sim в стартовой комнате.</summary>
    private static HashSet<WallKey> Plan(float x, float y)
    {
        GameFixture game = new();
        List<WallInstance> walls = ChunkMesh.BuildWalls(game.Sim.World, game.Sim.WorldTable, 0, 0);
        return CutawayPlan.Compute(game.Sim.World, new Vector3(x, 0, y), Back(), walls);
    }

    /// <summary>Направление на камеру у изометрической камеры.</summary>
    private static Vector3 Back()
    {
        IsoCamera camera = new();
        camera.Init(Vector3.Zero);
        Vector3 back = camera.Basis.Z;
        camera.Free();
        return back;
    }

    /// <summary>Сколько кусков стен стартовой комнаты закрывают персонажа, стоящего в клетке x, y по sim.</summary>
    private static int Occluding(float x, float y)
    {
        GameFixture game = new();
        Vector3 feet = new(x, 0, y);
        return ChunkMesh.BuildWalls(game.Sim.World, game.Sim.WorldTable, 0, 0).Count(wall => Cutaway.Occludes(wall.Transform, feet, Back()));
    }
}
