using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Стена или дверь чанка: положение бокса, цвет и вид.</summary>
public readonly record struct WallInstance(Transform3D Transform, Color Color, bool IsDoor, bool Open);

/// <summary>Геометрия чанка из данных мира: квады полов и боксы стен.</summary>
public static class ChunkMesh
{
    /// <summary>Высота стены.</summary>
    public const float WallHeight = 2.4f;

    /// <summary>Толщина стены.</summary>
    public const float WallThickness = 0.1f;

    /// <summary>Длина открытой двери, чтобы она не утыкалась в соседнюю стену.</summary>
    private const float OpenDoorLength = 0.9f;

    /// <summary>Угол открытой двери от закрытого положения.</summary>
    private const float OpenDoorAngle = 80f;

    /// <summary>Меш полов чанка с цветами вершин, null если полов нет.</summary>
    public static ArrayMesh? BuildFloors(World world, WorldTable table, int chunkX, int chunkY)
    {
        SurfaceTool tool = new();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        int quads = 0;
        foreach (CellPos cell in Cells(world, chunkX, chunkY, insideOnly: true))
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

    /// <summary>Стены и двери на северных и западных рёбрах клеток чанка.</summary>
    public static List<WallInstance> BuildWalls(World world, WorldTable table, int chunkX, int chunkY)
    {
        List<WallInstance> walls = [];
        foreach (CellPos cell in Cells(world, chunkX, chunkY, insideOnly: false))
        {
            AddWall(walls, table, world.GetEdge(cell, Side.North), cell, north: true);
            AddWall(walls, table, world.GetEdge(cell, Side.West), cell, north: false);
        }

        return walls;
    }

    /// <summary>Клетки чанка на всех этажах; insideOnly - только клетки карты, иначе и запас хранения под внешние рёбра.</summary>
    private static IEnumerable<CellPos> Cells(World world, int chunkX, int chunkY, bool insideOnly)
    {
        int extra = insideOnly ? 0 : 1;
        int maxX = Math.Min((chunkX + 1) * World.ChunkSize, world.Width + extra);
        int maxY = Math.Min((chunkY + 1) * World.ChunkSize, world.Height + extra);
        for (int z = World.MinLevel; z <= World.MaxLevel; z++)
        {
            for (int y = chunkY * World.ChunkSize; y < maxY; y++)
            {
                for (int x = chunkX * World.ChunkSize; x < maxX; x++)
                {
                    yield return new CellPos(x, y, z);
                }
            }
        }
    }

    /// <summary>Бокс стены или двери по ребру; открытая дверь повёрнута вокруг петли в начале ребра.</summary>
    private static void AddWall(List<WallInstance> walls, WorldTable table, Edge edge, CellPos cell, bool north)
    {
        if (edge.Wall == 0)
        {
            return;
        }

        WallDef wall = table.Wall(edge.Wall);
        bool isDoor = wall.Kind == WallKind.Door;
        Vector3 start = ViewMath.ToGodot(cell.X, cell.Y, cell.Z);

        // Северное ребро идёт вдоль X, западное - вдоль Z; открытая дверь поворачивается внутрь клетки
        Vector3 along = north ? Vector3.Right : Vector3.Back;
        Vector3 inside = north ? Vector3.Back : Vector3.Right;
        float length = 1f;
        if (isDoor && edge.Open)
        {
            float angle = Mathf.DegToRad(OpenDoorAngle);
            along = ((along * Mathf.Cos(angle)) + (inside * Mathf.Sin(angle))).Normalized();
            length = OpenDoorLength;
        }

        walls.Add(new WallInstance(Segment(start, along, length), Color.FromHtml(wall.Color), isDoor, edge.Open));
    }

    /// <summary>Положение единичного бокса, растянутого вдоль direction на length от start.</summary>
    private static Transform3D Segment(Vector3 start, Vector3 direction, float length)
    {
        Basis basis = new(direction * length, Vector3.Up * WallHeight, direction.Cross(Vector3.Up) * WallThickness);
        Vector3 center = start + (direction * length / 2) + (Vector3.Up * WallHeight / 2);
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
