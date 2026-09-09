using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Статичный класс генерации карты.
/// </summary>
public static class MapGen
{
    /// <summary>
    /// Метод возврата дампа карты в виде текста.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="starts">Стартовые гексы.</param>
    /// <returns>[TODO:return]</returns>
    public static string Dump(World world, IReadOnlyList<Hex> starts)
    {
        StringBuilder sb = new();
        for (int row = 0; row < world.Height; row++)
        {
            if ((row & 1) == 1)
            {
                sb.Append(' '); // сдвиг нечётного ряда — как на карте
            }

            for (int col = 0; col < world.Width; col++)
            {
                Hex hex = Hex.FromOffset(col, row);
                char glyph = starts.Contains(hex) ? '*' : Glyph(world.Type[hex]);
                if (col > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(glyph);
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }


    /// <summary>
    /// Метод расстановки цетров кластеров гексов.
    /// </summary>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="width">Ширина карты.</param>
    /// <param name="height">Высота карты.</param>
    /// <param name="count">Количество центров.</param>
    /// <param name="minDist">Минимальная дистанция между центрами кластеров.</param>
    /// <param name="inset">Отступ от края карты.</param>
    /// <returns>Вернет центральные гексы.</returns>
    internal static List<Hex> PlaceCenters(
        Rng rng, int width, int height,
        int count, int minDist, int inset)
    {
        const int MaxAttempts = 10_000;
        List<Hex> centers = [];
        for (int attempt = 0; attempt < MaxAttempts && centers.Count < count; attempt++)
        {
            // Берем случайный гекс с учётом отступа от краёв.
            int col = inset + rng.Next(width - (2 * inset));
            int row = inset + rng.Next(height - (2 * inset));
            Hex candidate = Hex.FromOffset(col, row);

            // Слишком близкие - отбрасываем.
            bool tooClose = centers.Any(c => candidate.DistanceTo(c) < minDist);
            if (tooClose)
            {
                continue;
            }
            centers.Add(candidate);
        }

        // Вернем результат.
        return centers.Count < count
            ? throw new InvalidOperationException($"cannot place {count} centers at distance {minDist} on {width}x{height}")
            : centers;
    }


    /// <summary>
    /// Метод возврата случайного значения по весам.
    /// </summary>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="weights">Веса.</param>
    /// <typeparam name="T">Целевой тип.</typeparam>
    /// <returns>Ключ целевого типа.</returns>
    internal static T PickWeighted<T>(Rng rng, IReadOnlyDictionary<T, float> weights)
        where T : notnull
    {
        // Порядок словаря не гарантирован, поэтому сортируем по ключу для детерминизма.
        List<KeyValuePair<T, float>> ordered =
            [.. weights.Where(static kv => kv.Value > 0f).OrderBy(static kv => kv.Key).ToList()];

        if (ordered.Count == 0)
        {
            throw new ArgumentException("no positive weights", nameof(weights));
        }

        // Кидаем кость.
        float roll = rng.NextFloat();
        foreach (KeyValuePair<T, float> kv in ordered)
        {
            if (roll < kv.Value)
            {
                return kv.Key;
            }
            roll -= kv.Value;
        }

        // Округление float проскочило последний кусок, поэтому вернем его.
        return ordered[^1].Key;
    }



    /// <summary>
    /// Метод заполнения кластера гексами.
    /// </summary>
    /// <param name="world">Информация о мире.</param>
    /// <param name="rules">Правила.</param>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="center">Гекс-центр кластера.</param>
    /// <param name="profile">Профиль кластера.</param>
    /// <param name="radius">Радиус кластера.</param>
    internal static void FillCluster(World world, Rules rules, Rng rng, Hex center, ClusterProfile profile, int radius)
    {
        Queue<Hex> queue = new();
        HashSet<Hex> seen = [center];
        queue.Enqueue(center);
        int nameIndex = 0;

        while (queue.Count > 0)
        {
            Hex hex = queue.Dequeue();

            // Соседей ставим в очередь до любых решений о самом гексе.
            // Дырка или занятый гекс не должны останавливать волну.
            for (int dir = 0; dir < Hex.Directions.Length; dir++)
            {
                Hex next = hex.Neighbor(dir);
                if (world.Type.Contains(next) && center.DistanceTo(next) <= radius && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }

            // Чужой узел и дырку пропускаем.
            bool isCenter = hex == center;
            if (world.IsNode(hex) || (!isCenter && rng.Chance(rules.MapGen.HoleChance)))
            {
                continue;
            }

            // Определим узел.
            NodeType type = isCenter && profile.Center is NodeType centerType
                ? centerType
                : PickWeighted(rng, profile.NodeWeights);
            NodeTypeDef def = rules.NodeTypes[type];

            // Даем рандомный бонус патча узлу.
            int patch = def.BasePatch + profile.PatchBonus;
            if (!isCenter)
            {
                patch += rng.Next(3) - 1; // -1, 0, 1.
            }

            world.Type[hex] = type;
            world.Os[hex] = PickWeighted(rng, def.OsWeights);
            world.Patch[hex] = Math.Clamp(patch, 0, 5);
            world.Name[hex] = $"{rng.Pick(profile.NamePool)} {nameIndex++}";
        }
    }


    /// <summary>
    /// Метод возврата символа узла.
    /// Удобно для отладки.
    /// </summary>
    /// <param name="type">Тип узла.</param>
    /// <returns>Символ.</returns>
    private static char Glyph(NodeType type) => type switch
    {
        NodeType.Empty => '.',
        NodeType.Home => 'h',
        NodeType.Workstation => 'w',
        NodeType.Server => 'S',
        NodeType.Router => 'R',
        NodeType.IoT => 'i',
        NodeType.Controller => 'C',
        _ => '?',
    };
}
