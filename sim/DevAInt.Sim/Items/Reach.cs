using System;
using DevAInt.Sim.Creatures;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Items;

/// <summary>Досягаемость объекта рукой: близко и не через стену, одно правило у сервера и окна инвентаря.</summary>
public static class Reach
{
    /// <summary>Наибольшее расстояние от центра тела до ближайшей точки объекта в метрах.</summary>
    public const double Distance = 1.2;

    /// <summary>Насколько точка цели отступает внутрь объекта от его края, чтобы луч кончался в клетке объекта.</summary>
    private const double Inset = 0.01;

    /// <summary>Достаёт ли тело в точке from до объекта obj того же этажа.</summary>
    public static bool CanReach(World world, WorldPos from, WorldObject obj) => DistanceTo(world, from, obj) is double distance && distance <= Distance;

    /// <summary>Расстояние до ближайшей точки объекта или null, если он на другом этаже или путь закрыт стеной или другим объектом.</summary>
    public static double? DistanceTo(World world, WorldPos from, WorldObject obj)
    {
        if (from.Z != obj.Cell.Z)
        {
            return null;
        }

        double x = Math.Clamp(from.X, obj.Cell.X + Inset, obj.Cell.X + obj.Width - Inset);
        double y = Math.Clamp(from.Y, obj.Cell.Y + Inset, obj.Cell.Y + obj.Depth - Inset);
        double dx = x - from.X;
        double dy = y - from.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));

        // Луч проверяется только вблизи: дальние объекты отсеиваются без прохода по клеткам
        if (distance > Distance)
        {
            return distance;
        }

        return Sight.Blocked(world, from, new WorldPos(x, y, from.Z)) is null ? distance : null;
    }
}
