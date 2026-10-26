using System;
using System.Collections.Generic;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;
using DevAInt.Sim.Items;
using DevAInt.Sim.Net;
using Side = DevAInt.Sim.Grid.Side;

/// <summary>Пункт меню постройки: что ставится или разбор, номер вида, название, состав и хватает ли материалов.</summary>
public sealed record BuildOption(BuildKind Kind, ushort Def, string Title, string Needs, bool Ready);

/// <summary>Данные режима постройки по реплике: пункты меню, цель под курсором и её проверка; ничего не рисует.</summary>
public static class BuildModel
{
    /// <summary>Ключ названия пункта разбора.</summary>
    public const string RemoveTitle = "ui.remove";

    /// <summary>Радиус тела существа без вида, как в sim.</summary>
    private const double DefaultRadius = 0.3;

    /// <summary>Пункты меню: строящиеся объекты, затем стены и двери, последним - разбор; готовность - по своему инвентарю.</summary>
    public static List<BuildOption> Options(Replica replica, ClientTables tables)
    {
        Dictionary<string, int> counts = Counts(replica, tables.Items);
        List<BuildOption> options = [];
        for (ushort i = 1; i <= tables.World.ObjectCount; i++)
        {
            if (tables.World.Object(i).Build is BuildDef build)
            {
                options.Add(Option(BuildKind.Object, i, tables.World.Object(i).Id, build, counts));
            }
        }

        for (ushort i = 1; i <= tables.World.WallCount; i++)
        {
            if (tables.World.Wall(i).Build is BuildDef build)
            {
                options.Add(Option(BuildKind.Wall, i, tables.World.Wall(i).Id, build, counts));
            }
        }

        options.Add(new BuildOption(BuildKind.RemoveObject, 0, GameLocale.Text(RemoveTitle, "Remove"), string.Empty, true));
        return options;
    }

    /// <summary>Цель пункта в точке пола point: клетка под ней, для стены - ближнее ребро этой клетки; разбор - объект в клетке, иначе стена на ближнем ребре; null - разбирать нечего.</summary>
    public static BuildTarget? Target(BuildOption option, World world, WorldPos point, int rotation)
    {
        CellPos cell = point.Cell;
        Side side = NearestSide(point);
        return option.Kind switch
        {
            BuildKind.Object => new BuildTarget(BuildKind.Object, option.Def, cell, rotation),
            BuildKind.Wall => new BuildTarget(BuildKind.Wall, option.Def, cell, (int)side),
            _ when world.ObjectAt(cell) is not null => new BuildTarget(BuildKind.RemoveObject, 0, cell, 0),
            _ when world.GetEdge(cell, side).Wall != 0 => new BuildTarget(BuildKind.RemoveWall, 0, cell, (int)side),
            _ => null,
        };
    }

    /// <summary>Почему цель не годится для тела в builder: то же правило Building.Problem, что у сервера, с телами всех сущностей реплики.</summary>
    public static string? Problem(Replica replica, ClientTables tables, BuildTarget target, WorldPos builder)
    {
        List<(WorldPos Position, double Radius)> bodies = [];
        foreach (ReplicaEntity entity in replica.Entities)
        {
            double radius = entity.Creature >= 0 ? tables.Creatures.Creature(entity.Creature).Radius : DefaultRadius;
            bodies.Add((new WorldPos(entity.X, entity.Y, entity.Z), radius));
        }

        return Building.Problem(replica.World!, tables.World, target, builder, bodies);
    }

    /// <summary>Сторона клетки, к ребру которой точка ближе всего.</summary>
    public static Side NearestSide(WorldPos point)
    {
        // Расстояния до четырёх рёбер внутри клетки: до северного - доля по y, до южного - остаток и так далее
        double x = point.X - Math.Floor(point.X);
        double y = point.Y - Math.Floor(point.Y);
        Side side = Side.North;
        double best = y;
        if (1 - y < best)
        {
            (side, best) = (Side.South, 1 - y);
        }

        if (x < best)
        {
            (side, best) = (Side.West, x);
        }

        return 1 - x < best ? Side.East : side;
    }

    /// <summary>Пункт постройки вида id с составом и готовностью.</summary>
    private static BuildOption Option(BuildKind kind, ushort index, string id, BuildDef build, Dictionary<string, int> counts)
    {
        List<string> needs = [];
        foreach (ItemCountDef input in build.Inputs)
        {
            needs.Add(input.Count > 1 ? $"{HudModel.Name(input.Item)} x{input.Count}" : HudModel.Name(input.Item));
        }

        foreach (string tool in build.Tools)
        {
            needs.Add(HudModel.Name(tool));
        }

        bool ready = Crafting.Missing(build.Inputs, build.Tools, counts) is null;
        return new BuildOption(kind, index, HudModel.Name(id), string.Join(", ", needs), ready);
    }

    /// <summary>Сколько предметов каждого вида в своём инвентаре.</summary>
    private static Dictionary<string, int> Counts(Replica replica, ItemTable table)
    {
        List<string> ids = [];
        foreach (ItemData item in replica.Inventory)
        {
            ids.Add(table.Item(item.Item).Id);
        }

        return Crafting.Counts(ids);
    }
}
