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

    /// <summary>Разобрать стену или дверь на ребре.</summary>
    RemoveWall,
}

/// <summary>Цель постройки: вид, номер вида в WorldTable для постановки, клетка и поворот объекта или сторона клетки для стены.</summary>
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

    /// <summary>Как строится цель: определение постройки её вида или null, если вид не строится или цели нет.</summary>
    public static BuildDef? Def(World world, WorldTable table, BuildTarget target)
    {
        return target.Kind switch
        {
            BuildKind.Object => target.Def >= 1 && target.Def <= table.ObjectCount ? table.Object(target.Def).Build : null,
            BuildKind.Wall => target.Def >= 1 && target.Def <= table.WallCount ? table.Wall(target.Def).Build : null,
            BuildKind.RemoveObject => world.ObjectAt(target.Cell) is WorldObject obj ? table.Object(obj.Def).Build : null,
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
            BuildKind.RemoveObject => RemoveObjectProblem(world, target, builder),
            _ => EdgeDistance(builder, target) <= Reach.Distance ? null : "out of reach",
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
            if (position.Z == target.Cell.Z && SegmentDistance(position, target) < radius)
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
    private static double EdgeDistance(WorldPos builder, BuildTarget target) => builder.Z == target.Cell.Z ? SegmentDistance(builder, target) : double.PositiveInfinity;

    /// <summary>Расстояние от точки до отрезка ребра клетки цели по полу.</summary>
    private static double SegmentDistance(WorldPos point, BuildTarget target)
    {
        // Отрезок ребра вдоль оси: ближайшая точка - зажатие координат
        int x = target.Cell.X;
        int y = target.Cell.Y;
        (double ax, double ay, double bx, double by) = target.Side switch
        {
            Side.North => (x, y, x + 1, y),
            Side.South => (x, y + 1, x + 1, y + 1),
            Side.West => (x, y, x, y + 1),
            _ => (x + 1, y, x + 1, y + 1),
        };
        double dx = Math.Clamp(point.X, ax, bx) - point.X;
        double dy = Math.Clamp(point.Y, ay, by) - point.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
