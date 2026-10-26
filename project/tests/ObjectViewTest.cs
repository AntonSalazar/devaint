using System.Collections.Generic;
using DevAInt.Sim.Grid;
using Godot;

/// <summary>Тесты вида объектов мира: боксы по размеру и повороту, узлы чанка, перестройка по сети и срез высоких объектов.</summary>
public class ObjectViewTest : TestCase
{
    /// <summary>Допуск координат.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>Бокс стола стартовой комнаты стоит на полу посередине своих двух клеток, на всю их площадь без зазора.</summary>
    public void TestBoxesFromStarterRoom()
    {
        GameFixture game = new();

        List<ObjectInstance> objects = ChunkMesh.BuildObjects(game.Sim.World, game.Sim.WorldTable, 0, 0);
        ObjectInstance table = objects.Find(obj => game.Sim.World.FindObject(obj.Id)!.Def == game.Sim.WorldTable.ObjectIndex("core:table"));

        CheckEqual(3, objects.Count, "three objects in the room");
        CheckNear(5.0, table.Transform.Origin.X, Tolerance, "table centre x");
        CheckNear(0.4, table.Transform.Origin.Y, Tolerance, "table stands on the floor");
        CheckNear(4.5, table.Transform.Origin.Z, Tolerance, "table centre z");
        CheckNear(2 - ChunkMesh.ObjectGap, table.Transform.Basis.Scale.X, Tolerance, "table width");
        CheckNear(0.8, table.Height, Tolerance, "table height");
    }

    /// <summary>Повёрнутый стол вытянут вдоль z.</summary>
    public void TestRotatedBox()
    {
        GameFixture game = new();
        ushort index = game.Sim.WorldTable.ObjectIndex("core:table");
        game.Sim.World.PlaceObject(WorldObject.Create(100, index, game.Sim.WorldTable.Object(index), new CellPos(2, 9, 0), 1));

        ObjectInstance table = ChunkMesh.BuildObjects(game.Sim.World, game.Sim.WorldTable, 0, 0).Find(obj => obj.Id == 100);

        CheckNear(1 - ChunkMesh.ObjectGap, table.Transform.Basis.Scale.X, Tolerance, "narrow along x");
        CheckNear(2 - ChunkMesh.ObjectGap, table.Transform.Basis.Scale.Z, Tolerance, "long along z");
        CheckNear(10, table.Transform.Origin.Z, Tolerance, "centre between its cells");
    }

    /// <summary>Чанк рисует объекты боксами со срезом, тени дают те же боксы целиком; поставленный и убранный объект перестраивает чанк.</summary>
    public void TestChunkDrawsAndRebuildsObjects()
    {
        GameFixture game = new();
        WorldView view = new();
        view.Init(game.Client.Replica, game.Sim.WorldTable, game.Sim.CreatureTable);
        game.Tick();
        ChunkView chunk = view.Chunk(0, 0)!;
        int before = chunk.VisibleObjects.Multimesh.InstanceCount;
        ushort crate = game.Sim.WorldTable.ObjectIndex("core:crate");

        game.Sim.World.PlaceObject(WorldObject.Create(100, crate, game.Sim.WorldTable.Object(crate), new CellPos(3, 3, 0), 0));
        game.Tick();
        int placed = chunk.Objects.Count;
        game.Sim.World.RemoveObject(100);
        game.Tick();

        CheckEqual(3, before, "room furniture");
        CheckEqual(4, placed, "placed crate is drawn");
        CheckEqual(3, chunk.Objects.Count, "removed crate is gone");
        Check(chunk.VisibleObjects.MaterialOverride == Cutaway.Material, "objects use the cutaway");
        CheckEqual(GeometryInstance3D.ShadowCastingSetting.ShadowsOnly, chunk.ShadowObjects.CastShadow, "whole boxes cast shadows");
        Check(chunk.VisibleObjects.Multimesh == chunk.ShadowObjects.Multimesh, "both share one multimesh");
        view.Free();
    }

    /// <summary>Высокий объект между персонажем и камерой режется, низкий и стоящий за спиной - нет.</summary>
    public void TestTallObjectInFrontIsCut()
    {
        Vector3 back = Back();
        Transform3D box = new(Basis.Identity.Scaled(new Vector3(0.94f, 2, 0.94f)), new Vector3(5.5f, 1, 5.5f));
        ObjectInstance wardrobe = new(box, Colors.Brown, 1, 2);
        ObjectInstance table = new(box, Colors.Brown, 2, 0.8f);

        HashSet<long> inFront = CutawayPlan.ComputeObjects(new Vector3(4.5f, 0, 4.5f), back, [wardrobe, table]);
        HashSet<long> behind = CutawayPlan.ComputeObjects(new Vector3(6.5f, 0, 6.5f), back, [wardrobe, table]);

        CheckEqual("1", string.Join(",", inFront), "only the tall object in front is cut");
        CheckEqual(0, behind.Count, "object behind the character stays");
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
}
