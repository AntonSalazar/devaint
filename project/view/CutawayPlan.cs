using System.Collections.Generic;
using DevAInt.Sim.Grid;
using Godot;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Какие куски стен опустить, как в Project Zomboid: стены своей комнаты к камере и отрезки стен, закрывающие персонажа.</summary>
public static class CutawayPlan
{
    /// <summary>Сколько клеток вдоль отрезка стены опускается по обе стороны от куска, закрывающего персонажа.</summary>
    public const int RunSpan = 2;

    /// <summary>Дальше этого расстояния от ног куски стен не проверяются на перекрытие.</summary>
    public const float OccluderRange = 8f;

    /// <summary>Объект ниже этой высоты не режется: он не закрывает персонажа целиком.</summary>
    public const float CutObjectHeight = 1.2f;

    /// <summary>Стороны клетки.</summary>
    private static readonly Side[] _sides = [Side.North, Side.East, Side.South, Side.West];

    /// <summary>Рёбра со стенами, которые опустить: персонаж стоит ногами в feet, камера смотрит против back.</summary>
    public static HashSet<WallKey> Compute(World world, Vector3 feet, Vector3 back, IEnumerable<WallInstance> walls)
    {
        HashSet<WallKey> cut = [];
        CellPos cell = new(Mathf.FloorToInt(feet.X), Mathf.FloorToInt(feet.Z), Mathf.RoundToInt(feet.Y / ViewMath.FloorHeight));
        if (Rooms.Find(world, cell) is HashSet<CellPos> room)
        {
            AddRoomWalls(world, room, back, cut);
        }

        // Стены других комнат и улицы режутся, только если закрывают персонажа, - но отрезком, а не одним куском
        foreach (WallInstance wall in walls)
        {
            if (wall.Transform.Origin.DistanceTo(feet) <= OccluderRange && Cutaway.Occludes(wall.Transform, feet, back))
            {
                AddRun(world, wall.Key, cut);
            }
        }

        return cut;
    }

    /// <summary>Id высоких объектов, которые закрывают персонажа с ногами в feet от камеры, смотрящей против back.</summary>
    public static HashSet<long> ComputeObjects(Vector3 feet, Vector3 back, IEnumerable<ObjectInstance> objects)
    {
        HashSet<long> cut = [];
        foreach (ObjectInstance obj in objects)
        {
            bool near = obj.Transform.Origin.DistanceTo(feet) <= OccluderRange;
            if (obj.Height >= CutObjectHeight && near && Cutaway.OccludesBox(obj.Transform, feet, back))
            {
                cut.Add(obj.Id);
            }
        }

        return cut;
    }

    /// <summary>Граница комнаты со стороны камеры: внутри комнаты видно всё, что в ней.</summary>
    private static void AddRoomWalls(World world, HashSet<CellPos> room, Vector3 back, HashSet<WallKey> cut)
    {
        foreach (CellPos cell in room)
        {
            foreach (Side side in _sides)
            {
                CellPos next = cell.Neighbor(side);
                if (!room.Contains(next) && world.GetEdge(cell, side).Wall != 0 && Outward(side).Dot(back) > 0)
                {
                    cut.Add(KeyOf(cell, side));
                }
            }
        }
    }

    /// <summary>Кусок и соседние куски того же прямого отрезка стены на RunSpan клеток в обе стороны, до первого разрыва.</summary>
    private static void AddRun(World world, WallKey key, HashSet<WallKey> cut)
    {
        cut.Add(key);
        foreach (int direction in new[] { -1, 1 })
        {
            for (int step = 1; step <= RunSpan; step++)
            {
                // Северные рёбра идут вдоль x, западные - вдоль y
                CellPos owner = key.North
                    ? key.Owner with { X = key.Owner.X + (direction * step) }
                    : key.Owner with { Y = key.Owner.Y + (direction * step) };
                if (world.GetEdge(owner, key.North ? Side.North : Side.West).Wall == 0)
                {
                    break;
                }

                cut.Add(new WallKey(owner, key.North));
            }
        }
    }

    /// <summary>Направление наружу от клетки через сторону, в осях Godot.</summary>
    private static Vector3 Outward(Side side) => side switch
    {
        Side.North => Vector3.Forward,
        Side.South => Vector3.Back,
        Side.East => Vector3.Right,
        _ => Vector3.Left,
    };

    /// <summary>Ребро стороны клетки как северное или западное ребро клетки-владельца.</summary>
    private static WallKey KeyOf(CellPos cell, Side side) => side switch
    {
        Side.North => new WallKey(cell, true),
        Side.South => new WallKey(cell.Neighbor(Side.South), true),
        Side.West => new WallKey(cell, false),
        _ => new WallKey(cell.Neighbor(Side.East), false),
    };
}
