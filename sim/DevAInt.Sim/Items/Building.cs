using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Items;

/// <summary>Что делается постройкой: ставится объект или стена, разбирается объект или стена.</summary>
public enum BuildKind : byte
{
    /// <summary>Поставить объект.</summary>
    Object,

    /// <summary>Поставить стену или дверь на ребро.</summary>
    Wall,

    /// <summary>Разобрать объект в клетке.</summary>
    RemoveObject,

    /// <summary>Разобрать стену на ребре; створку сначала нужно снять.</summary>
    RemoveWall,

    /// <summary>Поставить дверь или окно в пустой проём или раму на ребре.</summary>
    Fill,

    /// <summary>Снять дверь или окно с ребра.</summary>
    RemoveFill,
}

/// <summary>Цель постройки: вид, номер вида в WorldTable для постановки, клетка и поворот объекта или сторона клетки для стены и створки.</summary>
public sealed record BuildTarget(BuildKind Kind, ushort Def, CellPos Cell, int Turn)
{
    /// <summary>Сторона клетки для стены.</summary>
    public Side Side => (Side)Turn;
}

/// <summary>Правила места постройки и разбора: одно правило у сервера и призрака постройки в клиенте.</summary>
public static class Building
{
    /// <summary>Доля материалов, которая возвращается при разборе, с округлением вниз.</summary>
    public const double RemoveShare = 0.5;

    /// <summary>Рёбра, которые хранит клетка.</summary>
    private static readonly Side[] _storedSides = [Side.North, Side.West];

    /// <summary>Как строится цель: определение постройки её вида или null, если вид не строится или цели нет.</summary>
    public static BuildDef? Def(World world, WorldTable table, BuildTarget target)
    {
        return target.Kind switch
        {
            BuildKind.Object => target.Def >= 1 && target.Def <= table.ObjectCount ? table.Object(target.Def).Build : null,
            BuildKind.Wall => target.Def >= 1 && target.Def <= table.WallCount ? table.Wall(target.Def).Build : null,
            BuildKind.Fill => target.Def >= 1 && target.Def <= table.FillCount ? table.Fill(target.Def).Build : null,
            BuildKind.RemoveObject => world.ObjectAt(target.Cell) is WorldObject obj ? table.Object(obj.Def).Build : null,
            BuildKind.RemoveFill => world.GetEdge(target.Cell, target.Side).Fill is ushort fill and > 0 ? table.Fill(fill).Build : null,
            _ => world.GetEdge(target.Cell, target.Side).Wall is ushort wall and > 0 ? table.Wall(wall).Build : null,
        };
    }

    /// <summary>Материалы, которые вернёт разбор: половина каждого с округлением вниз.</summary>
    public static List<ItemCountDef> Refund(BuildDef def)
    {
        List<ItemCountDef> refund = [];
        foreach (ItemCountDef input in def.Inputs)
        {
            int count = (int)Math.Floor(input.Count * RemoveShare);
            if (count > 0)
            {
                refund.Add(input with { Count = count });
            }
        }

        return refund;
    }

    /// <summary>Почему цель нельзя построить или разобрать телом в builder при телах bodies; null - можно. Материалы проверяет тот, у кого инвентарь.</summary>
    public static string? Problem(World world, WorldTable table, BuildTarget target, WorldPos builder, IEnumerable<(WorldPos Position, double Radius)> bodies)
    {
        if (target.Turn is < 0 or >= WorldObject.Rotations)
        {
            return $"turn {target.Turn} must be within 0..3";
        }

        if (Def(world, table, target) is null)
        {
            return "this cannot be built or removed";
        }

        return target.Kind switch
        {
            BuildKind.Object => ObjectProblem(world, table, target, builder, bodies),
            BuildKind.Wall => WallProblem(world, target, builder, bodies),
            BuildKind.Fill => FillProblem(world, table, target, builder, bodies),
            BuildKind.RemoveObject => RemoveObjectProblem(world, target, builder),
            BuildKind.RemoveWall when world.GetEdge(target.Cell, target.Side).Fill != 0 => "remove the door or window first",
            _ => EdgeDistance(builder, target) <= Reach.Distance ? null : "out of reach",
        };
    }

    /// <summary>Ближняя к телу в точке at створка на расстоянии руки, впереди по взгляду facing: клетка и сторона ребра; null - рядом нет.</summary>
    public static (CellPos Cell, Side Side)? NearestFill(World world, WorldPos at, (double X, double Y) facing)
    {
        (CellPos Cell, Side Side)? best = null;
        double bestDistance = double.MaxValue;
        CellPos center = at.Cell;
        for (int dy = -1; dy <= 2; dy++)
        {
            for (int dx = -1; dx <= 2; dx++)
            {
                // Каждая клетка хранит северное и западное ребро: так каждое ребро рядом проверяется один раз
                CellPos cell = new(center.X + dx, center.Y + dy, at.Z);
                foreach (Side side in _storedSides)
                {
                    double distance = SegmentDistance(at, cell, side);
                    if (world.GetEdge(cell, side).Fill != 0 && distance <= Reach.Distance && distance < bestDistance && InFront(at, cell, side, facing))
                    {
                        (best, bestDistance) = ((cell, side), distance);
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Расстояние от точки до отрезка ребра side клетки cell по полу.</summary>
    public static double SegmentDistance(WorldPos point, CellPos cell, Side side)
    {
        // Отрезок ребра вдоль оси: ближайшая точка - зажатие координат
        (double ax, double ay, double bx, double by) = Segment(cell, side);
        double dx = Math.Clamp(point.X, ax, bx) - point.X;
        double dy = Math.Clamp(point.Y, ay, by) - point.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Створка: в ребре пустой проём или рама нужного створке вида, в проёме никто не стоит, рука достаёт.</summary>
    private static string? FillProblem(World world, WorldTable table, BuildTarget target, WorldPos builder, IEnumerable<(WorldPos Position, double Radius)> bodies)
    {
        FillDef fill = table.Fill(target.Def);
        Edge edge = world.GetEdge(target.Cell, target.Side);
        if (edge.Wall == 0 || edge.Kind != fill.FrameKind)
        {
            return fill.FrameKind == WallKind.DoorFrame ? "needs a door frame" : "needs a window frame";
        }

        if (edge.Fill != 0)
        {
            return "the frame is already filled";
        }

        foreach ((WorldPos position, double radius) in bodies)
        {
            if (position.Z == target.Cell.Z && SegmentDistance(position, target.Cell, target.Side) < radius)
            {
                return "someone stands there";
            }
        }

        return EdgeDistance(builder, target) <= Reach.Distance ? null : "out of reach";
    }

    /// <summary>Середина ребра впереди тела по взгляду или совсем рядом.</summary>
    private static bool InFront(WorldPos at, CellPos cell, Side side, (double X, double Y) facing)
    {
        (double ax, double ay, double bx, double by) = Segment(cell, side);
        double dx = ((ax + bx) / 2) - at.X;
        double dy = ((ay + by) / 2) - at.Y;
        return (dx * facing.X) + (dy * facing.Y) >= 0 || (dx * dx) + (dy * dy) < 0.25;
    }

    /// <summary>Концы отрезка ребра side клетки cell.</summary>
    private static (double Ax, double Ay, double Bx, double By) Segment(CellPos cell, Side side)
    {
        int x = cell.X;
        int y = cell.Y;
        return side switch
        {
            Side.North => (x, y, x + 1, y),
            Side.South => (x, y + 1, x + 1, y + 1),
            Side.West => (x, y, x, y + 1),
            _ => (x + 1, y, x + 1, y + 1),
        };
    }

    /// <summary>Объект: место по правилам мира, никто не стоит на клетках, рука достаёт.</summary>
    private static string? ObjectProblem(World world, WorldTable table, BuildTarget target, WorldPos builder, IEnumerable<(WorldPos Position, double Radius)> bodies)
    {
        WorldObject obj = WorldObject.Create(0, target.Def, table.Object(target.Def), target.Cell, target.Turn);
        if (!world.Contains(target.Cell))
        {
            return "outside the world";
        }

        if (world.PlaceProblem(obj) is string problem)
        {
            return problem;
        }

        foreach ((WorldPos position, double radius) in bodies)
        {
            double dx = Math.Clamp(position.X, obj.Cell.X, obj.Cell.X + obj.Width) - position.X;
            double dy = Math.Clamp(position.Y, obj.Cell.Y, obj.Cell.Y + obj.Depth) - position.Y;
            if (position.Z == obj.Cell.Z && (dx * dx) + (dy * dy) < radius * radius)
            {
                return "someone stands there";
            }
        }

        return Reach.CanReach(world, builder, obj) ? null : "out of reach";
    }

    /// <summary>Стена: ребро пустое, хотя бы с одной стороны пол, объект не режется, никто не стоит на ребре, рука достаёт.</summary>
    private static string? WallProblem(World world, BuildTarget target, WorldPos builder, IEnumerable<(WorldPos Position, double Radius)> bodies)
    {
        CellPos other = target.Cell.Neighbor(target.Side);
        if (!world.IsWalkable(target.Cell) && !world.IsWalkable(other))
        {
            return "no floor at the edge";
        }

        if (world.GetEdge(target.Cell, target.Side).Wall != 0)
        {
            return "the edge already has a wall";
        }

        if (world.ObjectAt(target.Cell) is WorldObject obj && world.ObjectAt(other) == obj)
        {
            return "the wall would cut an object";
        }

        foreach ((WorldPos position, double radius) in bodies)
        {
            if (position.Z == target.Cell.Z && SegmentDistance(position, target.Cell, target.Side) < radius)
            {
                return "someone stands there";
            }
        }

        return EdgeDistance(builder, target) <= Reach.Distance ? null : "out of reach";
    }

    /// <summary>Разбор объекта: контейнер пуст, рука достаёт.</summary>
    private static string? RemoveObjectProblem(World world, BuildTarget target, WorldPos builder)
    {
        WorldObject obj = world.ObjectAt(target.Cell)!;
        if (world.Container(obj.Id) is Inventory { Items.Count: > 0 })
        {
            return "the container is not empty";
        }

        return Reach.CanReach(world, builder, obj) ? null : "out of reach";
    }

    /// <summary>Расстояние от тела до ребра цели; другой этаж - бесконечность.</summary>
    private static double EdgeDistance(WorldPos builder, BuildTarget target) => builder.Z == target.Cell.Z ? SegmentDistance(builder, target.Cell, target.Side) : double.PositiveInfinity;
}
