using DevAInt.Sim.Defs;
using DevAInt.Sim.Logging;

namespace DevAInt.Sim.Grid;

/// <summary>Постройка мира по карте из мода.</summary>
public static class MapBuilder
{
    /// <summary>Новый мир по проверенной карте: операции build по порядку; объект, которому нет места, - ошибка в лог, объект пропускается.</summary>
    public static World Build(MapDef map, WorldTable table, ISimLog? log = null)
    {
        World world = new(map.Width, map.Height);
        for (int i = 0; i < map.Build.Count; i++)
        {
            MapOpDef op = map.Build[i];
            if (op.Floor is string floor)
            {
                FillFloor(world, op, table.FloorIndex(floor));
            }
            else if (op.Walls is string walls)
            {
                Outline(world, op, new Edge(table.WallIndex(walls), false));
            }
            else if (op.Object is string id)
            {
                // Место зависит от прошлых операций, поэтому его проверяет постройка, а не проверка карты при загрузке
                ushort index = table.ObjectIndex(id);
                WorldObject obj = WorldObject.Create(world.NextObjectId, index, table.Object(index), new CellPos(op.X, op.Y, op.Z), op.Rotation);
                if (world.PlaceProblem(obj) is string problem)
                {
                    log?.Error($"map {map.Id}: build[{i}]: object {id}: {problem}");
                    continue;
                }

                world.PlaceObject(obj);
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
