using DevAInt.Sim.Defs;

namespace DevAInt.Sim.Grid;

/// <summary>Постройка мира по карте из мода.</summary>
public static class MapBuilder
{
    /// <summary>Новый мир по проверенной карте: операции build по порядку.</summary>
    public static World Build(MapDef map, WorldTable table)
    {
        World world = new(map.Width, map.Height);
        foreach (MapOpDef op in map.Build)
        {
            if (op.Floor is string floor)
            {
                FillFloor(world, op, table.FloorIndex(floor));
            }
            else if (op.Walls is string walls)
            {
                Outline(world, op, new Edge(table.WallIndex(walls), false));
            }
            else
            {
                world.SetEdge(new CellPos(op.X, op.Y, op.Z), op.Side!.Value, new Edge(table.WallIndex(op.Door!), op.Open));
            }
        }

        return world;
    }

    /// <summary>Пол на все клетки прямоугольника.</summary>
    private static void FillFloor(World world, MapOpDef op, ushort floor)
    {
        for (int y = op.Y; y < op.Y + op.H; y++)
        {
            for (int x = op.X; x < op.X + op.W; x++)
            {
                world.SetFloor(new CellPos(x, y, op.Z), floor);
            }
        }
    }

    /// <summary>Стены по внешнему контуру прямоугольника.</summary>
    private static void Outline(World world, MapOpDef op, Edge wall)
    {
        for (int x = op.X; x < op.X + op.W; x++)
        {
            world.SetEdge(new CellPos(x, op.Y, op.Z), Side.North, wall);
            world.SetEdge(new CellPos(x, op.Y + op.H - 1, op.Z), Side.South, wall);
        }

        for (int y = op.Y; y < op.Y + op.H; y++)
        {
            world.SetEdge(new CellPos(op.X, y, op.Z), Side.West, wall);
            world.SetEdge(new CellPos(op.X + op.W - 1, y, op.Z), Side.East, wall);
        }
    }
}
