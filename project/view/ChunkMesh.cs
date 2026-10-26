using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Motion;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Кусок стены, дверь или стекло окна чанка: положение бокса, цвет, вид и ребро.</summary>
public readonly record struct WallInstance(Transform3D Transform, Color Color, bool IsDoor, bool Open, WallKey Key, bool IsGlass = false);

/// <summary>Объект мира чанка: положение бокса, цвет, id, высота и лестница ли это: лестница - наклонная плита, её срез не трогает.</summary>
public readonly record struct ObjectInstance(Transform3D Transform, Color Color, long Id, float Height, bool IsStairs = false);

/// <summary>Ребро клетки, на котором стоит кусок стены: клетка-владелец и северное ли это ребро.</summary>
public readonly record struct WallKey(CellPos Owner, bool North);

/// <summary>Геометрия чанка из данных мира: квады полов, боксы стен и объектов.</summary>
public static class ChunkMesh
{
    /// <summary>Высота стены: во весь этаж, чтобы стены доходили до пола этажа выше и он не висел в воздухе.</summary>
    public const float WallHeight = ViewMath.FloorHeight;

    /// <summary>Толщина стены.</summary>
    public const float WallThickness = 0.1f;

    /// <summary>Высота двери и низ перемычки над дверным проёмом.</summary>
    public const float DoorHeight = 2.2f;

    /// <summary>Высота подоконника: низ окна.</summary>
    public const float SillHeight = 0.9f;

    /// <summary>Верх окна и низ перемычки над ним.</summary>
    public const float WindowTop = 2.0f;

    /// <summary>Толщина стекла окна.</summary>
    public const float GlassThickness = 0.02f;

    /// <summary>Толщина двери: тоньше стены, чтобы на стыке грани стены стояли перед гранями двери, а не в одной плоскости с ними.</summary>
    public const float DoorThickness = 0.06f;

    /// <summary>Зазор между боксом объекта и краем его клеток: соседние объекты не сливаются в один.</summary>
    public const float ObjectGap = 0.06f;

    /// <summary>Длина открытой двери, чтобы она не утыкалась в соседнюю стену.</summary>
    private const float OpenDoorLength = 0.9f;

    /// <summary>Угол открытой двери от закрытого положения.</summary>
    private const float OpenDoorAngle = 80f;

    /// <summary>Толщина плиты лестницы.</summary>
    private const float StairsThickness = 0.15f;

    /// <summary>Меш полов этажа z чанка с цветами вершин, null если полов нет.</summary>
    public static ArrayMesh? BuildFloors(World world, WorldTable table, int chunkX, int chunkY, int z)
    {
        SurfaceTool tool = new();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        int quads = 0;
        foreach (CellPos cell in Cells(world, chunkX, chunkY, insideOnly: true, z))
        {
            ushort floor = world.Floor(cell);
            if (floor == 0)
            {
                continue;
            }

            AddQuad(tool, ViewMath.ToGodot(cell.X, cell.Y, cell.Z), Color.FromHtml(table.Floor(floor).Color));
            quads++;
        }

        return quads == 0 ? null : tool.Commit();
    }

    /// <summary>Стены и двери на северных и западных рёбрах клеток этажа z чанка.</summary>
    public static List<WallInstance> BuildWalls(World world, WorldTable table, int chunkX, int chunkY, int z)
    {
        List<WallInstance> walls = [];
        foreach (CellPos cell in Cells(world, chunkX, chunkY, insideOnly: false, z))
        {
            AddWall(walls, table, world.GetEdge(cell, Side.North), cell, north: true);
            AddWall(walls, table, world.GetEdge(cell, Side.West), cell, north: false);
        }

        return walls;
    }

    /// <summary>Боксы объектов этажа z, чья северо-западная клетка лежит в чанке: на всю площадь клеток без зазора и на высоту вида; лестница - наклонная плита.</summary>
    public static List<ObjectInstance> BuildObjects(World world, WorldTable table, int chunkX, int chunkY, int z)
    {
        List<ObjectInstance> objects = [];
        foreach (WorldObject obj in world.ChunkObjects(chunkX, chunkY))
        {
            if (obj.Cell.Z != z)
            {
                continue;
            }

            ObjectDef def = table.Object(obj.Def);
            if (obj.IsStairs)
            {
                objects.Add(new ObjectInstance(StairsSlab(obj), Color.FromHtml(def.Color), obj.Id, ViewMath.FloorHeight, IsStairs: true));
                continue;
            }

            float height = (float)def.Height;
            Vector3 size = new(obj.Width - ObjectGap, height, obj.Depth - ObjectGap);
            Vector3 center = ViewMath.ToGodot(obj.Cell.X + (obj.Width / 2f), obj.Cell.Y + (obj.Depth / 2f), obj.Cell.Z) + (Vector3.Up * height / 2);
            objects.Add(new ObjectInstance(new Transform3D(Basis.Identity.Scaled(size), center), Color.FromHtml(def.Color), obj.Id, height));
        }

        return objects;
    }

    /// <summary>Плита лестницы от низа у нижнего края до этажа выше у верхнего, во всю ширину клетки.</summary>
    private static Transform3D StairsSlab(WorldObject stairs)
    {
        Vector3 up = Stairs.Up(stairs) switch
        {
            Side.North => Vector3.Forward,
            Side.South => Vector3.Back,
            Side.East => Vector3.Right,
            _ => Vector3.Left,
        };
        float length = Math.Max(stairs.Width, stairs.Depth);
        Vector3 along = ((up * length) + (Vector3.Up * ViewMath.FloorHeight)).Normalized();
        Vector3 side = Vector3.Up.Cross(up).Normalized();
        Vector3 normal = along.Cross(side).Normalized();
        float slope = new Vector2(length, ViewMath.FloorHeight).Length();
        Basis basis = new(side * (1 - ObjectGap), normal * StairsThickness, along * slope);

        // Середина плиты - над серединой лестницы на половине высоты этажа
        Vector3 center = ViewMath.ToGodot(stairs.Cell.X + (stairs.Width / 2f), stairs.Cell.Y + (stairs.Depth / 2f), stairs.Cell.Z) + (Vector3.Up * ViewMath.FloorHeight / 2);
        return new Transform3D(basis, center);
    }

    /// <summary>Клетки этажа z чанка; insideOnly - только клетки карты, иначе и запас хранения под внешние рёбра.</summary>
    private static IEnumerable<CellPos> Cells(World world, int chunkX, int chunkY, bool insideOnly, int z)
    {
        int extra = insideOnly ? 0 : 1;
        int maxX = Math.Min((chunkX + 1) * World.ChunkSize, world.Width + extra);
        int maxY = Math.Min((chunkY + 1) * World.ChunkSize, world.Height + extra);
        for (int y = chunkY * World.ChunkSize; y < maxY; y++)
        {
            for (int x = chunkX * World.ChunkSize; x < maxX; x++)
            {
                yield return new CellPos(x, y, z);
            }
        }
    }

    /// <summary>Куски ребра по виду стены: сплошная стена; перемычка над проёмом и дверь; подоконник, перемычка и стекло окна.</summary>
    private static void AddWall(List<WallInstance> walls, WorldTable table, Edge edge, CellPos cell, bool north)
    {
        if (edge.Wall == 0)
        {
            return;
        }

        Color color = Color.FromHtml(table.Wall(edge.Wall).Color);
        Color fill = edge.Fill == 0 ? color : Color.FromHtml(table.Fill(edge.Fill).Color);
        Vector3 start = ViewMath.ToGodot(cell.X, cell.Y, cell.Z);
        WallKey key = new(cell, north);

        // Северное ребро идёт вдоль X, западное - вдоль Z
        Vector3 along = north ? Vector3.Right : Vector3.Back;
        switch (edge.Kind)
        {
            case WallKind.DoorFrame:
                walls.Add(Piece(start, along, DoorHeight, WallHeight, color, key));
                if (edge.Fill != 0)
                {
                    walls.Add(Door(start, along, north ? Vector3.Back : Vector3.Right, edge.Open, fill, key));
                }

                break;
            case WallKind.WindowFrame:
                walls.Add(Piece(start, along, 0, SillHeight, color, key));
                walls.Add(Piece(start, along, WindowTop, WallHeight, color, key));
                if (edge.Fill != 0)
                {
                    // Открытое окно поднято вверх, как в Project Zomboid: верхняя половина стекла уходит за перемычку
                    float lift = edge.Open ? (WindowTop - SillHeight) / 2 : 0;
                    Transform3D pane = Segment(start, along, 1f, GlassThickness, SillHeight + lift, WindowTop - SillHeight);
                    walls.Add(new WallInstance(pane, fill, false, edge.Open, key, IsGlass: true));
                }

                break;
            default:
                walls.Add(Piece(start, along, 0, WallHeight, color, key));
                break;
        }
    }

    /// <summary>Кусок стены по всей длине ребра от высоты bottom до top.</summary>
    private static WallInstance Piece(Vector3 start, Vector3 along, float bottom, float top, Color color, WallKey key)
    {
        // Кусок длиннее ребра на толщину стены: соседние куски перекрываются, без стыков и щелей в углах
        Transform3D transform = Segment(start - (along * WallThickness / 2), along, 1f + WallThickness, WallThickness, bottom, top - bottom);
        return new WallInstance(transform, color, false, false, key);
    }

    /// <summary>Дверь в проёме: закрытая - в плоскости ребра, открытая - повёрнута вокруг петли в начале ребра внутрь клетки.</summary>
    private static WallInstance Door(Vector3 start, Vector3 along, Vector3 inside, bool open, Color color, WallKey key)
    {
        // Дверь не удлиняется и тоньше стены: иначе её грани совпали бы с гранями перемычки и мерцали
        Vector3 direction = along;
        float length = 1f;
        if (open)
        {
            float angle = Mathf.DegToRad(OpenDoorAngle);
            direction = ((along * Mathf.Cos(angle)) + (inside * Mathf.Sin(angle))).Normalized();
            length = OpenDoorLength;
        }

        return new WallInstance(Segment(start, direction, length, DoorThickness, 0, DoorHeight), color, true, open, key);
    }

    /// <summary>Положение единичного бокса, растянутого вдоль direction на length от start, толщиной thickness, от высоты bottom на height вверх.</summary>
    private static Transform3D Segment(Vector3 start, Vector3 direction, float length, float thickness, float bottom, float height)
    {
        Basis basis = new(direction * length, Vector3.Up * height, direction.Cross(Vector3.Up) * thickness);
        Vector3 center = start + (direction * length / 2) + (Vector3.Up * (bottom + (height / 2)));
        return new Transform3D(basis, center);
    }

    /// <summary>Квад клетки 1 на 1 м лицом вверх.</summary>
    private static void AddQuad(SurfaceTool tool, Vector3 corner, Color color)
    {
        Vector3 a = corner;
        Vector3 b = corner + Vector3.Right;
        Vector3 c = corner + Vector3.Right + Vector3.Back;
        Vector3 d = corner + Vector3.Back;
        tool.SetColor(color);
        tool.SetNormal(Vector3.Up);

        // Обход по часовой стрелке при взгляде сверху: в Godot это лицевая сторона
        foreach (Vector3 vertex in new[] { a, b, c, a, c, d })
        {
            tool.AddVertex(vertex);
        }
    }
}
