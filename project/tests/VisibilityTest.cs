using System;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Тесты видимости на клиенте: текстура затемнения, скрытие чужих существ, плавное появление.</summary>
public class VisibilityTest : TestCase
{
    /// <summary>Обзор для тестов: конус 90 градусов на 10 м, взгляд на юг.</summary>
    private static readonly VisionDef _vision = new() { Angle = 90, Range = 10 };

    /// <summary>Материал прохода использует шейдер видимости, неизведанное залито цветом фона.</summary>
    public void TestShaderIsLoaded()
    {
        VisibilityOverlay overlay = new();
        overlay.Init();

        CheckEqual(VisibilityOverlay.ShaderPath, ((ShaderMaterial)overlay.MaterialOverride).Shader.ResourcePath, "shader path");
        CheckEqual(Lighting.Background, overlay.Parameter("unexplored").AsColor(), "unexplored is the background colour");
        overlay.Free();
    }

    /// <summary>В текстуре стен - биты загораживающих рёбер и объектов; открытая дверь не загораживает.</summary>
    public void TestWallTexels()
    {
        World world = new(64, 64);
        world.SetEdge(new CellPos(30, 20, 0), Side.North, new Edge(1, false));
        world.SetEdge(new CellPos(30, 20, 0), Side.West, new Edge(1, false));
        world.SetEdge(new CellPos(31, 20, 0), Side.East, new Edge(1, false));
        world.SetEdge(new CellPos(33, 20, 0), Side.West, new Edge(1, true));
        world.SetFloor(new CellPos(35, 20, 0), 1);
        world.PlaceObject(WorldObject.Create(1, 1, new ObjectDef { Id = "test:wardrobe", BlocksSight = true }, new CellPos(35, 20, 0), 0));
        CellPos origin = Origin(30, 30);

        byte[] texels = Walls(world, origin);

        CheckEqual((byte)(VisibilityOverlay.NorthWall | VisibilityOverlay.WestWall), Wall(texels, origin, 30, 20), "north and west walls");
        CheckEqual(VisibilityOverlay.WestWall, Wall(texels, origin, 32, 20), "east wall is the west wall of the neighbour");
        CheckEqual((byte)0, Wall(texels, origin, 33, 20), "open door does not block");
        CheckEqual(VisibilityOverlay.SightBlock, Wall(texels, origin, 35, 20), "wardrobe blocks sight");
    }

    /// <summary>В текстуре памяти 4 на 4 тексела на клетку: увиденная клетка вся, точка края по своему биту, невиденная - 0.</summary>
    public void TestMemoryTexels()
    {
        World world = new(64, 64);
        VisionTracker tracker = new();
        tracker.Update(world, new WorldPos(30.5, 30.5, 0), (0, 1), _vision);
        tracker.Update(world, new WorldPos(30.5, 30.5, 0), (0, -1), _vision);
        CellPos origin = Origin(30, 30);
        byte[] texels = new byte[VisibilityOverlay.MemoryTexels * VisibilityOverlay.MemoryTexels];

        VisibilityOverlay.FillMemory(texels, tracker.Seen, origin);

        CheckEqual((byte)255, Memory(texels, origin, 30, 35, 0, 0), "seen before, first point");
        CheckEqual((byte)255, Memory(texels, origin, 30, 35, 3, 3), "seen before, last point");
        CheckEqual((byte)0, Memory(texels, origin, 45, 30, 1, 1), "never seen");

        // Клетка на краю конуса 90 градусов на юг: южный запад в конусе, северный восток нет
        CellPos edge = new(35, 35, 0);
        ushort points = tracker.Seen.Points(edge);
        Check(points is not 0 and not SeenCells.AllPoints, "edge cell is partly seen");
        CheckEqual((byte)255, Memory(texels, origin, 35, 35, 0, 3), "south west point");
        CheckEqual((byte)0, Memory(texels, origin, 35, 35, 3, 0), "north east point");
    }

    /// <summary>Луч по стенам из текстуры, как в шейдере, совпадает с Sight.Clear в sim на случайных точках среди комнат.</summary>
    public void TestTexelRayMatchesSight()
    {
        World world = new(64, 64);
        Random random = new(7);
        for (int i = 0; i < 300; i++)
        {
            CellPos cell = new(random.Next(10, 50), random.Next(10, 50), 0);
            world.SetEdge(cell, random.Next(2) == 0 ? Side.North : Side.East, new Edge(1, random.Next(5) == 0));
        }

        // Шкафы среди стен: объект закрывает обзор так же, как в sim; объекту нужен пол
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                world.SetFloor(new CellPos(x, y, 0), 1);
            }
        }

        ObjectDef wardrobe = new() { Id = "test:wardrobe", BlocksSight = true };
        int placed = 0;
        for (int i = 0; i < 60; i++)
        {
            WorldObject obj = WorldObject.Create(i + 1, 1, wardrobe, new CellPos(random.Next(10, 50), random.Next(10, 50), 0), 0);
            if (world.PlaceProblem(obj) is null && obj.Cell != new CellPos(30, 30, 0))
            {
                world.PlaceObject(obj);
                placed++;
            }
        }

        WorldPos eye = new(30.37, 30.81, 0);
        CellPos origin = Origin(30, 30);
        byte[] texels = Walls(world, origin);
        int mismatches = 0;
        for (int i = 0; i < 2000; i++)
        {
            WorldPos point = new(eye.X + (random.NextDouble() * 50) - 25, eye.Y + (random.NextDouble() * 50) - 25, 0);
            if (Sight.Clear(world, eye, point) != TexelClear(texels, origin, eye, point))
            {
                mismatches++;
            }
        }

        Check(placed > 30, $"{placed} wardrobes placed");
        CheckEqual(0, mismatches, "texel ray mismatches");
    }

    /// <summary>Полоса действия и призрак постройки рисуются после затемнения: иначе оно затирает их копией кадра.</summary>
    public void TestOverlayDrawsBeforeMarks()
    {
        VisibilityOverlay overlay = new();
        overlay.Init();
        EntityView entity = new();
        entity.Init(1, true, Vector3.Zero, Colors.Blue);
        BuildGhost ghost = new();
        ghost.Init();
        int under = overlay.MaterialOverride.RenderPriority;

        Check(entity.GetNode<MeshInstance3D>("ActionBar").MaterialOverride.RenderPriority > under, "action bar is drawn after the overlay");
        Check(entity.GetNode<Label3D>("ActionCaption").RenderPriority > under, "action caption is drawn after the overlay");
        Check(ghost.MaterialOverride.RenderPriority > under, "build ghost is drawn after the overlay");
        overlay.Free();
        entity.Free();
        ghost.Free();
    }

    /// <summary>Глаза, взгляд и обзор уходят в параметры шейдера.</summary>
    public void TestLookSetsParameters()
    {
        VisibilityOverlay overlay = new();
        overlay.Init();

        overlay.Look(new Vector3(3.5f, 1, 7.25f), new Vector3(0, 0, 2), new VisionDef { Angle = 140, Range = 25, Near = 1.5 });

        CheckEqual(new Vector2(3.5f, 7.25f), overlay.Parameter("eye").AsVector2(), "eye from x and z");
        CheckEqual(new Vector2(0, 1), overlay.Parameter("facing").AsVector2(), "facing normalized");
        CheckNear(Mathf.DegToRad(70), overlay.Parameter("half_angle").AsSingle(), 1e-5, "half angle");
        CheckNear(25, overlay.Parameter("range").AsSingle(), 1e-5, "range");
        CheckNear(1.5, overlay.Parameter("near").AsSingle(), 1e-5, "near");
        overlay.Free();
    }

    /// <summary>Чужая капсула вне видимости гаснет и пропадает, своя видна всегда; без карты видно всё.</summary>
    public void TestEntitiesOutsideVisionFade()
    {
        GameFixture game = new();
        WorldView view = new();
        view.Init(game.Client.Replica, game.Sim.WorldTable, game.Sim.CreatureTable);
        long behind = game.Sim.CreateEntity(new WorldPos(6.5, 4.5, 0), "core:human");
        long ahead = game.Sim.CreateEntity(new WorldPos(6.5, 8.5, 0), "core:human");
        game.Tick();
        VisionTracker tracker = new();
        tracker.Update(game.Client.Replica.World!, new WorldPos(6.5, 6.5, 0), (0, 1), _vision);

        view.UpdateVisibility(tracker.Map);
        view.Advance(EntityView.FadeSeconds / 2);
        float halfway = view.Entity(behind)!.Shown;
        view.Advance(EntityView.FadeSeconds);

        CheckNear(0.5, halfway, 1e-3, "fades out over time");
        Check(!view.Entity(behind)!.Visible, "behind is hidden");
        Check(view.Entity(ahead)!.Visible, "ahead stays");
        Check(view.Entity(game.Client.Replica.OwnEntityId)!.Visible, "own stays");

        view.UpdateVisibility(null);
        view.Advance(1);
        Check(view.Entity(behind)!.Visible, "everything shown without a vision map");
        view.Free();
    }

    /// <summary>Угол окна с центром в клетке x, y.</summary>
    private static CellPos Origin(int x, int y) => new(x - (VisibilityOverlay.WindowCells / 2), y - (VisibilityOverlay.WindowCells / 2), 0);

    /// <summary>Повтор функции clear из шейдера: проход по клеткам и проверка битов стен в байтах окна.</summary>
    private static bool TexelClear(byte[] texels, CellPos origin, WorldPos from, WorldPos to)
    {
        int cellX = (int)Math.Floor(from.X);
        int cellY = (int)Math.Floor(from.Y);
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        int stepX = Math.Sign(dx);
        int stepY = Math.Sign(dy);
        double deltaX = dx == 0 ? 1e30 : 1 / Math.Abs(dx);
        double deltaY = dy == 0 ? 1e30 : 1 / Math.Abs(dy);
        double nextX = dx == 0 ? 1e30 : (stepX > 0 ? cellX + 1 - from.X : from.X - cellX) * deltaX;
        double nextY = dy == 0 ? 1e30 : (stepY > 0 ? cellY + 1 - from.Y : from.Y - cellY) * deltaY;
        int steps = Math.Abs((int)Math.Floor(to.X) - cellX) + Math.Abs((int)Math.Floor(to.Y) - cellY);
        for (int i = 0; i < steps; i++)
        {
            if (nextX < nextY)
            {
                if ((Wall(texels, origin, stepX > 0 ? cellX + 1 : cellX, cellY) & VisibilityOverlay.WestWall) != 0)
                {
                    return false;
                }

                cellX += stepX;
                nextX += deltaX;
            }
            else
            {
                if ((Wall(texels, origin, cellX, stepY > 0 ? cellY + 1 : cellY) & VisibilityOverlay.NorthWall) != 0)
                {
                    return false;
                }

                cellY += stepY;
                nextY += deltaY;
            }

            bool atEnd = cellX == (int)Math.Floor(to.X) && cellY == (int)Math.Floor(to.Y);
            if (!atEnd && (Wall(texels, origin, cellX, cellY) & VisibilityOverlay.SightBlock) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Байты стен окна с углом origin.</summary>
    private static byte[] Walls(World world, CellPos origin)
    {
        byte[] texels = new byte[VisibilityOverlay.WindowCells * VisibilityOverlay.WindowCells];
        VisibilityOverlay.FillWalls(texels, world, origin);
        return texels;
    }

    /// <summary>Байт стен клетки x, y в окне с углом origin.</summary>
    private static byte Wall(byte[] texels, CellPos origin, int x, int y) => texels[((y - origin.Y) * VisibilityOverlay.WindowCells) + (x - origin.X)];

    /// <summary>Байт памяти точки px, py клетки x, y в окне с углом origin.</summary>
    private static byte Memory(byte[] texels, CellPos origin, int x, int y, int px, int py)
    {
        int side = SeenCells.PointsPerSide;
        return texels[((((y - origin.Y) * side) + py) * VisibilityOverlay.MemoryTexels) + ((x - origin.X) * side) + px];
    }
}
