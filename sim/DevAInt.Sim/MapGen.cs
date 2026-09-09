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
    /// Id жилого профиля кластера в данных.
    /// </summary>
    private const string ResidentialProfile = "residential";

    /// <summary>
    /// Id офисного профиля кластера в данных.
    /// </summary>
    private const string OfficeProfile = "office";

    /// <summary>
    /// Id профиля датацентра в данных.
    /// </summary>
    private const string DatacenterProfile = "datacenter";

    /// <summary>
    /// Id профиля завода в данных.
    /// </summary>
    private const string FactoryProfile = "factory";

    /// <summary>
    /// Сколько сидов подряд пробуем, если карта не прошла валидацию.
    /// </summary>
    private const int MaxRetries = 20;

    /// <summary>
    /// Отступ центров кластеров от края карты.
    /// </summary>
    private const int CenterInset = 2;

    /// <summary>
    /// Минимальная дистанция между концами линка: линк должен вести в другой кластер,
    /// иначе это не обход, а дубль соседства.
    /// </summary>
    private const int MinLinkDist = 6;

    /// <summary>
    /// Сколько раз пробуем подобрать пару узлов под линк.
    /// </summary>
    private const int LinkAttempts = 50;


    /// <summary>
    /// Метод генерации карты от сида.
    /// Если карта не прошла валидацию, пробует следующий сид.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="seed">Сид.</param>
    /// <param name="factions">Количество фракций.</param>
    /// <returns>Вернет сгенерированную карту.</returns>
    public static MapResult Generate(Rules rules, int seed, int factions)
    {
        MapSizeDef size = rules.MapSizeFor(factions);
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            MapResult? result = TryGenerate(rules, size, seed + retry, factions);
            if (result is not null)
            {
                return result;
            }
        }

        throw new InvalidOperationException($"no valid map for seed {seed} and {factions} factions after {MaxRetries} retries");
    }


    /// <summary>
    /// Метод возврата дампа карты в виде текста.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="starts">Стартовые гексы.</param>
    /// <returns>Вернет карту символами, строка на ряд.</returns>
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
    /// <returns>Вернет соседей.</returns>
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


    /// <summary>
    /// Метод одной попытки генерации карты.
    /// </summary>
    /// <param name="rules">Правила игры.</param>
    /// <param name="size">Параметры карты для этого числа фракций.</param>
    /// <param name="seed">Сид попытки.</param>
    /// <param name="factions">Количество фракций.</param>
    /// <returns>Вернет карту или null, если центры не разместились или валидация не прошла.</returns>
    private static MapResult? TryGenerate(Rules rules, MapSizeDef size, int seed, int factions)
    {
        Rng rng = new(seed);
        World world = new(size.Width, size.Height, factions);

        // Центры кластеров. Не влезли - пробуем другой сид.
        List<Hex> centers;
        try
        {
            centers = PlaceCenters(rng, size.Width, size.Height, size.Clusters, rules.MapGen.MinCenterDist, CenterInset);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        // Роли кластеров: старты уходят в начало списка центров.
        string[] roles = AssignRoles(rng, size, centers, factions);

        // Заливка.
        int radiusSpan = rules.MapGen.ClusterRadiusMax - rules.MapGen.ClusterRadiusMin + 1;
        for (int i = 0; i < centers.Count; i++)
        {
            int radius = rules.MapGen.ClusterRadiusMin + rng.Next(radiusSpan);
            FillCluster(world, rules, rng, centers[i], rules.Profiles[roles[i]], radius);
        }

        // Коридоры.
        foreach ((int a, int b) in SpanningEdges(centers, size.ExtraCorridors))
        {
            BuildCorridor(world, rules, rng, centers[a], centers[b]);
        }

        // Линки.
        AddLinks(world, rng, size.Links);

        // Старты - гарантированный Home с базовой защитой.
        List<Hex> starts = [.. centers.Take(factions)];
        NodeTypeDef home = rules.NodeTypes[NodeType.Home];
        foreach (Hex start in starts)
        {
            world.Type[start] = NodeType.Home;
            world.Os[start] = PickWeighted(rng, home.OsWeights);
            world.Patch[start] = home.BasePatch;
        }

        // Вернем карту, если она прошла валидацию.
        return IsValid(world, starts, size) ? new MapResult(world, starts, seed) : null;
    }


    /// <summary>
    /// Метод назначения ролей кластерам.
    /// Старты - farthest-point (каждый следующий максимально далек от уже выбранных),
    /// датацентры - максимально далеки от всех стартов, остальное - случайно.
    /// Стартовые центры переставляются в начало <paramref name="centers"/>.
    /// </summary>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="size">Параметры карты.</param>
    /// <param name="centers">Центры кластеров; порядок меняется.</param>
    /// <param name="factions">Количество фракций.</param>
    /// <returns>Вернет Id профиля для каждого центра в новом порядке.</returns>
    private static string[] AssignRoles(Rng rng, MapSizeDef size, List<Hex> centers, int factions)
    {
        List<int> free = [.. Enumerable.Range(0, centers.Count)];

        // Старты.
        List<int> starts = [TakeRandom(rng, free)];
        while (starts.Count < factions)
        {
            int best = free.MaxBy(i => starts.Min(s => centers[i].DistanceTo(centers[s])));
            starts.Add(best);
            free.Remove(best);
        }

        // Датацентры - спорные, подальше от всех стартов.
        List<int> datacenters = [];
        while (datacenters.Count < size.Datacenters && free.Count > 0)
        {
            int best = free.MaxBy(i => starts.Min(s => centers[i].DistanceTo(centers[s])));
            datacenters.Add(best);
            free.Remove(best);
        }

        // Раздаем роли.
        string[] roles = new string[centers.Count];
        foreach (int i in starts)
        {
            roles[i] = ResidentialProfile;
        }
        foreach (int i in datacenters)
        {
            roles[i] = DatacenterProfile;
        }
        for (int k = 0; k < size.Factories && free.Count > 0; k++)
        {
            roles[TakeRandom(rng, free)] = FactoryProfile;
        }
        for (int k = 0; k < factions && free.Count > 0; k++)
        {
            roles[TakeRandom(rng, free)] = OfficeProfile;
        }
        while (free.Count > 0)
        {
            roles[TakeRandom(rng, free)] = rng.Chance(0.5f) ? ResidentialProfile : OfficeProfile;
        }

        // Старты - в начало, чтобы Starts[i] была фракция i.
        int[] order = [.. starts, .. Enumerable.Range(0, centers.Count).Where(i => !starts.Contains(i))];
        List<Hex> reorderedCenters = [.. order.Select(i => centers[i])];
        string[] reorderedRoles = [.. order.Select(i => roles[i])];
        centers.Clear();
        centers.AddRange(reorderedCenters);
        return reorderedRoles;
    }


    /// <summary>
    /// Метод изъятия случайного элемента из списка.
    /// </summary>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="free">Список; выбранный элемент из него удаляется.</param>
    /// <returns>Вернет изъятый элемент.</returns>
    private static int TakeRandom(Rng rng, List<int> free)
    {
        int picked = free[rng.Next(free.Count)];
        free.Remove(picked);
        return picked;
    }


    /// <summary>
    /// Метод добавления дальних линков по счетчикам из данных.
    /// Backbone - между самыми дальними роутерами, Vpn - сервер с сервером,
    /// Sneakernet - дом с офисной машиной.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="counts">Сколько линков каждого вида.</param>
    private static void AddLinks(World world, Rng rng, LinkCounts counts)
    {
        List<Hex> routers = NodesOfType(world, NodeType.Router);
        List<Hex> servers = NodesOfType(world, NodeType.Server);
        List<Hex> homes = NodesOfType(world, NodeType.Home);
        List<Hex> workstations = NodesOfType(world, NodeType.Workstation);

        // Backbone: пары роутеров по убыванию дистанции.
        List<(Hex A, Hex B)> routerPairs = [];
        for (int a = 0; a < routers.Count; a++)
        {
            for (int b = a + 1; b < routers.Count; b++)
            {
                routerPairs.Add((routers[a], routers[b]));
            }
        }
        routerPairs.Sort((x, y) => y.A.DistanceTo(y.B).CompareTo(x.A.DistanceTo(x.B)));
        foreach ((Hex a, Hex b) in routerPairs.Where(p => p.A.DistanceTo(p.B) >= MinLinkDist).Take(counts.Backbone))
        {
            world.Links.Add(new Link(a, b, LinkKind.Backbone));
        }

        // Vpn и Sneakernet: случайные пары.
        for (int k = 0; k < counts.Vpn; k++)
        {
            TryAddLink(world, rng, servers, servers, LinkKind.Vpn);
        }
        for (int k = 0; k < counts.Sneakernet; k++)
        {
            TryAddLink(world, rng, homes, workstations, LinkKind.Sneakernet);
        }
    }


    /// <summary>
    /// Метод подбора случайной пары узлов под линк.
    /// Концы не должны быть соседями по сетке и не должны повторять существующий линк.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="rng">Экземпляр генератора.</param>
    /// <param name="from">Кандидаты на первый конец.</param>
    /// <param name="to">Кандидаты на второй конец.</param>
    /// <param name="kind">Вид линка.</param>
    /// <returns>Флаг, добавлен ли линк.</returns>
    private static bool TryAddLink(World world, Rng rng, List<Hex> from, List<Hex> to, LinkKind kind)
    {
        if (from.Count == 0 || to.Count == 0)
        {
            return false;
        }

        for (int attempt = 0; attempt < LinkAttempts; attempt++)
        {
            Hex a = rng.Pick(from);
            Hex b = rng.Pick(to);
            bool duplicate = world.Links.Any(l => (l.A == a && l.B == b) || (l.A == b && l.B == a));
            if (a.DistanceTo(b) < MinLinkDist || duplicate)
            {
                continue;
            }

            world.Links.Add(new Link(a, b, kind));
            return true;
        }

        return false;
    }


    /// <summary>
    /// Метод возврата всех узлов заданного типа в порядке обхода карты.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="type">Тип узла.</param>
    /// <returns>Вернет список гексов.</returns>
    private static List<Hex> NodesOfType(World world, NodeType type) =>
        [.. world.Nodes().Where(h => world.Type[h] == type)];


    /// <summary>
    /// Метод валидации карты: все узлы связаны, старты не ближе минимальной дистанции.
    /// </summary>
    /// <param name="world">Инфа о мире.</param>
    /// <param name="starts">Стартовые гексы.</param>
    /// <param name="size">Параметры карты.</param>
    /// <returns>Флаг, годится ли карта.</returns>
    private static bool IsValid(World world, List<Hex> starts, MapSizeDef size)
    {
        // Волна от первого старта должна накрыть все узлы.
        int nodes = world.Nodes().Count();
        int reached = Search.Bfs(starts[0], h => world.Neighbors(h).Where(world.IsNode)).Count();
        if (reached != nodes)
        {
            return false;
        }

        // Старты не ближе минимальной дистанции.
        for (int i = 0; i < starts.Count; i++)
        {
            for (int j = i + 1; j < starts.Count; j++)
            {
                if (starts[i].DistanceTo(starts[j]) < size.MinStartDist)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
