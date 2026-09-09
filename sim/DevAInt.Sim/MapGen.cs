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
        int nameIndex = 0;
        IEnumerable<Hex> disk = Search.Bfs(center, hex => InRadius(world, hex, center, radius));

        foreach (Hex hex in disk)
        {
            bool isCenter = hex == center;
            if (world.IsNode(hex) || (!isCenter && rng.Chance(rules.MapGen.HoleChance)))
            {
                continue;
            }

            NodeType type = isCenter && profile.Center is NodeType centerType
                ? centerType
                : PickWeighted(rng, profile.NodeWeights);
            NodeTypeDef def = rules.NodeTypes[type];

            int patch = def.BasePatch + profile.PatchBonus;
            if (!isCenter)
            {
                patch += rng.Next(3) - 1;
            }

            world.Type[hex] = type;
            world.Os[hex] = PickWeighted(rng, def.OsWeights);
            world.Patch[hex] = Math.Clamp(patch, 0, 5);
            world.Name[hex] = $"{rng.Pick(profile.NamePool)} {nameIndex++}";
        }
    }


    /// <summary>
    /// Метод объединения кластеров.
    /// </summary>
    /// <param name="centers">Список центров, которые будут соединены.</param>
    /// <param name="extraEdges">Дополнительные ребра для обходных путей.</param>
    /// <returns>Список кортежей объедененных кластеров.</returns>
    internal static List<(int A, int B)> SpanningEdges(IReadOnlyList<Hex> centers, int extraEdges)
    {
        List<(int A, int B)> edges = [];
        HashSet<int> inTree = [0];

        // Пока не все в дереве — добавляем кратчайшее ребро.
        while (inTree.Count < centers.Count)
        {
            (int A, int B) best = (-1, -1);
            int bestDist = int.MaxValue;
            for (int a = 0; a < centers.Count; a++)
            {
                if (!inTree.Contains(a))
                {
                    continue;
                }

                for (int b = 0; b < centers.Count; b++)
                {
                    int dist = centers[a].DistanceTo(centers[b]);
                    if (!inTree.Contains(b) && dist < bestDist)
                    {
                        best = (a, b);
                        bestDist = dist;
                    }
                }
            }

            edges.Add(best);
            inTree.Add(best.B);
        }

        // Лишние рёбра — кратчайшие из оставшихся, для обходных путей.
        List<(int A, int B)> candidates = [];
        for (int a = 0; a < centers.Count; a++)
        {
            for (int b = a + 1; b < centers.Count; b++)
            {
                if (!edges.Contains((a, b)) && !edges.Contains((b, a)))
                {
                    candidates.Add((a, b));
                }
            }
        }

        candidates.Sort((x, y) => centers[x.A].DistanceTo(centers[x.B]).CompareTo(centers[y.A].DistanceTo(centers[y.B])));
        edges.AddRange(candidates.Take(extraEdges));
        return edges;
    }


    /// <summary>
    /// Метод постройки коридоров в виде линий.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="rules">Правила игры.</param>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="from">От какого гекса строим коридор.</param>
    /// <param name="to">Целевой гекс, где коридор заканчивается.</param>
    internal static void BuildCorridor(World world, Rules rules, Rng rng, Hex from, Hex to)
    {
        int placed = 0;
        foreach (Hex hex in from.LineTo(to))
        {
            if (!world.Type.Contains(hex) || world.IsNode(hex))
            {
                continue;
            }

            NodeType type = placed % 2 == 0 ? NodeType.Router : NodeType.IoT;
            NodeTypeDef def = rules.NodeTypes[type];
            world.Type[hex] = type;
            world.Os[hex] = PickWeighted(rng, def.OsWeights);
            world.Patch[hex] = def.BasePatch;
            world.Name[hex] = $"edge {placed}";
            placed++;
        }
    }


    /// <summary>
    /// Метод возврата флага, соединены ли два гекса между собой.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="from">Откуда начинается коридор.</param>
    /// <param name="to">Где заканчивается коридор.</param>
    /// <returns>Флаг соединения.</returns>
    internal static bool Connected(World world, Hex from, Hex to) =>
        world.IsNode(from)
        && world.IsNode(to)
        && Search.Bfs(from, hex => world.Neighbors(hex).Where(world.IsNode)).Contains(to);


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


    /// <summary>
    /// Метод возврата списка 6 соседей внутри карты
    /// и не дальше <paramref name="radius"/> от <paramref name="center"/>.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="hex">У кого берем соседей.</param>
    /// <param name="center">Центральный гекс у кластера.</param>
    /// <param name="radius">Радиус поиска.</param>
    /// <returns>[TODO:return]</returns>
    private static IEnumerable<Hex> InRadius(World world, Hex hex, Hex center, int radius)
    {
        for (int dir = 0; dir < Hex.Directions.Length; dir++)
        {
            Hex next = hex.Neighbor(dir);
            if (world.Type.Contains(next) && center.DistanceTo(next) <= radius)
            {
                yield return next;
            }
        }
    }
}
