using System;
using DevAInt.Sim.Defs;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Что видят глаза: клетки в конусе взгляда и в радиусе вокруг, если их не загораживают стены и закрытые двери.</summary>
public static class Vision
{
    /// <summary>Точки внутри клетки, по которым считается доля её видимости: центр и четыре ближе к углам.</summary>
    private static readonly (double X, double Y)[] _samples = [(0.5, 0.5), (0.2, 0.2), (0.8, 0.2), (0.2, 0.8), (0.8, 0.8)];

    /// <summary>Видимость вокруг глаз eye при взгляде facing по обзору vision в карту map.</summary>
    public static void Compute(World world, WorldPos eye, (double X, double Y) facing, VisionDef vision, VisibilityMap map)
    {
        int radius = (int)Math.Ceiling(vision.Range);
        CellPos center = eye.Cell;
        map.Reset(center, radius);

        // Сравнение косинусов вместо углов: точка в конусе, если косинус угла до взгляда не меньше косинуса полуширины
        double halfCos = vision.Angle >= 360 ? -1 : Math.Cos(vision.Angle * Math.PI / 360);
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                CellPos cell = new(center.X + dx, center.Y + dy, center.Z);
                int seen = 0;
                foreach ((double sx, double sy) in _samples)
                {
                    WorldPos point = new(cell.X + sx, cell.Y + sy, cell.Z);
                    if (InView(eye, point, facing, vision, halfCos) && Sight.Clear(world, eye, point))
                    {
                        seen++;
                    }
                }

                map.Set(dx, dy, (float)seen / _samples.Length);
            }
        }

        // Своя клетка видна всегда: глаза стоят в ней
        map.Set(0, 0, 1);
    }

    /// <summary>Точка в пределах дальности и в конусе взгляда или в радиусе вокруг.</summary>
    private static bool InView(WorldPos eye, WorldPos point, (double X, double Y) facing, VisionDef vision, double halfCos)
    {
        double dx = point.X - eye.X;
        double dy = point.Y - eye.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance > vision.Range)
        {
            return false;
        }

        return distance <= vision.Near || distance < 1e-9 || ((dx * facing.X) + (dy * facing.Y)) / distance >= halfCos;
    }
}
