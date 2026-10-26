using System.Collections.Generic;
using System.Linq;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Net;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Тесты режима постройки в клиенте: пункты меню, цель под курсором, проверка как у сервера, призрак и меню.</summary>
public class BuildViewTest : TestCase
{
    /// <summary>Допуск координат.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>Ящик, стена, дверь и разбор; ящик готов, только когда есть доски, гвозди и молоток.</summary>
    public void TestOptions()
    {
        GameFixture game = new();
        game.Tick();
        ClientTables tables = ClientTables.From(game.Sim);
        List<BuildOption> before = BuildModel.Options(game.Client.Replica, tables);
        Give(game, "core:hammer", "core:plank", "core:plank", "core:plank", "core:plank", "core:nails", "core:nails", "core:nails", "core:nails");
        game.Tick();

        List<BuildOption> after = BuildModel.Options(game.Client.Replica, tables);

        CheckEqual("Object Wall Wall RemoveObject", string.Join(" ", after.Select(option => option.Kind)), "kinds");
        Check(!before[0].Ready, "crate is not ready without materials");
        Check(after[0].Ready, "crate is ready with materials");
        Check(after[^1].Ready, "remove is always offered");
    }

    /// <summary>Ребро клетки - ближнее к точке; разбор берёт объект в клетке, иначе стену на ближнем ребре, иначе ничего.</summary>
    public void TestTargets()
    {
        GameFixture game = new();
        game.Tick();
        World world = game.Client.Replica.World!;
        BuildOption remove = new(BuildKind.RemoveObject, 0, "Remove", string.Empty, true);
        BuildOption wall = new(BuildKind.Wall, game.Sim.WorldTable.WallIndex("core:wood_wall"), "Wall", string.Empty, true);

        CheckEqual(Side.East, BuildModel.NearestSide(new WorldPos(6.9, 6.5, 0)), "east");
        CheckEqual(Side.North, BuildModel.NearestSide(new WorldPos(6.5, 6.1, 0)), "north");
        CheckEqual((int)Side.South, BuildModel.Target(wall, world, new WorldPos(6.5, 6.8, 0), 0)!.Turn, "wall on the nearest edge");
        CheckEqual(BuildKind.RemoveObject, BuildModel.Target(remove, world, new WorldPos(5.5, 4.5, 0), 0)?.Kind, "table is removed");
        CheckEqual(BuildKind.RemoveWall, BuildModel.Target(remove, world, new WorldPos(6.5, 4.1, 0), 0)?.Kind, "room wall is removed");
        CheckEqual(null, BuildModel.Target(remove, world, new WorldPos(6.5, 6.5, 0), 0), "nothing to remove in the middle");
    }

    /// <summary>Проверка цели в клиенте даёт то же, что сервер: на себе нельзя, рядом можно.</summary>
    public void TestProblemLikeServer()
    {
        GameFixture game = new();
        game.Tick();
        ClientTables tables = ClientTables.From(game.Sim);
        WorldPos own = game.Client.OwnPosition!.Value;
        ushort crate = tables.World.ObjectIndex("core:crate");

        string? onSelf = BuildModel.Problem(game.Client.Replica, tables, new BuildTarget(BuildKind.Object, crate, own.Cell, 0), own);
        string? next = BuildModel.Problem(game.Client.Replica, tables, new BuildTarget(BuildKind.Object, crate, own.Cell with { X = own.Cell.X + 1 }, 0), own);

        CheckEqual("someone stands there", onSelf, "own cell");
        CheckEqual(null, next, "next cell");
    }

    /// <summary>Призрак ящика стоит на полу своей клетки, призрак стены - на ребре во всю высоту; без цели призрак скрыт.</summary>
    public void TestGhost()
    {
        GameFixture game = new();
        WorldTable table = game.Sim.WorldTable;
        Transform3D crate = BuildGhost.Box(new BuildTarget(BuildKind.Object, table.ObjectIndex("core:crate"), new CellPos(7, 6, 0), 0), game.Sim.World, table)!.Value;
        Transform3D wall = BuildGhost.Box(new BuildTarget(BuildKind.Wall, table.WallIndex("core:wood_wall"), new CellPos(6, 6, 0), (int)Side.South), game.Sim.World, table)!.Value;
        BuildGhost ghost = new();
        ghost.Init();

        ghost.Show(crate, true);
        bool shown = ghost.Visible;
        Color good = ghost.Color;
        ghost.Show(null, false);

        CheckNear(0, crate.Origin.DistanceTo(new Vector3(7.5f, 0.3f, 6.5f)), Tolerance, "crate centre");
        CheckNear(0, wall.Origin.DistanceTo(new Vector3(6.5f, ChunkMesh.WallHeight / 2, 7)), Tolerance, "wall on the south edge");
        CheckNear(1, wall.Basis.Scale.X, Tolerance, "wall along x");
        CheckEqual(null, BuildGhost.Box(new BuildTarget(BuildKind.RemoveObject, 0, new CellPos(6, 6, 0), 0), game.Sim.World, table), "nothing to remove");
        Check(shown && !ghost.Visible, "shown with a target and hidden without");
        CheckEqual(BuildGhost.Good, good, "green when it can be built");
        Check(((BaseMaterial3D)ghost.MaterialOverride).RenderPriority > 0, "ghost is drawn after the visibility overlay");
        ghost.Free();
    }

    /// <summary>Меню показывает пункты с составом, выбор отдаёт пункт, неготовый пункт выключен.</summary>
    public void TestMenu()
    {
        BuildMenu menu = new();
        menu.Init();
        BuildOption crate = new(BuildKind.Object, 1, "Crate", "Plank x4", false);
        BuildOption remove = new(BuildKind.RemoveObject, 0, "Remove", string.Empty, true);
        List<BuildOption> chosen = [];
        menu.Chosen += chosen.Add;

        menu.Show([crate, remove]);
        menu.Choose(1);

        CheckEqual("Crate  (Plank x4)", menu.Lines[1], "row with needs");
        CheckEqual("Remove", menu.Lines[2], "remove row");
        CheckEqual(remove, chosen.Single(), "chosen");
        menu.Free();
    }

    /// <summary>Предметы в инвентарь своей сущности на сервере.</summary>
    private static void Give(GameFixture game, params string[] items)
    {
        foreach (string item in items)
        {
            game.Sim.GiveItem(game.Client.Replica.OwnEntityId, game.Sim.ItemTable.Item(game.Sim.ItemTable.ItemIndex(item)));
        }
    }
}
