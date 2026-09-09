using System.Collections.Generic;

using DevAInt.Sim.Data;

namespace DevAInt.Sim;


/// <summary>
/// Структура дальнего линка между не соседними гексами.
/// </summary>
/// <param name="A">Откуда начинается линк.</param>
/// <param name="B">Где заканчивается.</param>
/// <param name="Kind">Тип связи.</param>
public sealed record Link(Hex A, Hex B, LinkKind Kind)
{
    /// <summary>
    /// Сколько ходов будет отключен линк. 0 - работает.
    /// </summary>
    public int DisabledUntilTurn { get; set; }
}


/// <summary>
/// Класс карты.
/// Набор слоёв одного размера и списка линков.
/// </summary>
public sealed class World
{
    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="width">Размер карты в ширину.</param>
    /// <param name="height">Размер карты в высоту.</param>
    /// <param name="factions">Сколько фракций в игре.</param>
    public World(int width, int height, int factions)
    {
        // Определим размеры карты.
        Width = width;
        Height = height;

        // Заполняем слои.
        Type = new(width, height);
        Os = new(width, height);
        Patch = new(width, height);
        Hardening = new(width, height);
        Owner = new(width, height, fill: -1); // По-умолчанию ничьё.
        Noise = new(width, height);
        Name = new(width, height, fill: string.Empty);

        // Заполняем туман войны для фракций.
        KnownBy = new Layer<bool>[factions];
        for (int i = 0; i < factions; i++)
        {
            KnownBy[i] = new(width, height);
        }
        Links = [];
    }


    /// <summary>
    /// Размер карты в ширину.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Размер карты в высоту.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Слой узлов на карте.
    /// </summary>
    public Layer<NodeType> Type { get; }

    /// <summary>
    /// Слой OS на карте.
    /// </summary>
    public Layer<Os> Os { get; }

    /// <summary>
    /// Слой патчей на карте.
    /// </summary>
    public Layer<int> Patch { get; }

    /// <summary>
    /// Слой укреплений на карте.
    /// </summary>
    public Layer<int> Hardening { get; }

    /// <summary>
    /// Слой владельцев узлов на карте.
    /// </summary>
    public Layer<int> Owner { get; }

    /// <summary>
    /// Слой шума на карте.
    /// </summary>
    public Layer<float> Noise { get; }

    /// <summary>
    /// Слои тумана войны на каждую фракцию в игре.
    /// </summary>
    public Layer<bool>[] KnownBy { get; }

    /// <summary>
    /// Слой с названиями.
    /// </summary>
    public Layer<string> Name { get; }

    /// <summary>
    /// Список узлов связи.
    /// </summary>
    public List<Link> Links { get; }


    /// <summary>
    /// Метод проверки, является ли целевой гекс узлом.
    /// </summary>
    /// <param name="h">Целевой гекс.</param>
    /// <returns>Флаг, является ли гекс узлом.</returns>
    public bool IsNode(Hex h) => Type.Contains(h) && Type[h] != NodeType.Empty;


    /// <summary>
    /// Метод возврата списка всех ячеек.
    /// </summary>
    /// <returns>Список всех ячеек.</returns>
    public IEnumerable<Hex> Cells()
    {
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                yield return Hex.FromOffset(col, row);
            }
        }
    }


    /// <summary>
    /// Метод возврата списка узлов (не пустые клетки).
    /// </summary>
    /// <returns>Вернет список узлов</returns>
    public IEnumerable<Hex> Nodes()
    {
        foreach (Hex h in Cells())
        {
            if (IsNode(h))
            {
                yield return h;
            }
        }
    }


    /// <summary>
    /// Метод возврата списка соседей у целевого гекса.
    /// Если сосед линк, то вернет его дальний гекс.
    /// </summary>
    /// <param name="h">Целевой гекс.</param>
    /// <returns>Список гексов-соседей.</returns>
    public IEnumerable<Hex> Neighbors(Hex h)
    {
        for (int dir = 0; dir < Hex.Directions.Length; dir++)
        {
            Hex n = h.Neighbor(dir);
            if (Type.Contains(n))
            {
                yield return n;
            }
        }

        foreach (Link link in Links)
        {
            if (link.A == h)
            {
                yield return link.B;
            }
            else if (link.B == h)
            {
                yield return link.A;
            }
        }
    }

}
