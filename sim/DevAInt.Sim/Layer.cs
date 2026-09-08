using System;

namespace DevAInt.Sim;

/// <summary>
/// Лист данных, наложенный на карту.
/// </summary>
/// <typeparam name="T">Тип данных на карте.</typeparam>
public sealed class Layer<T>
{
    /// <summary>
    /// Ячейки, заполненные данными.
    /// </summary>
    private readonly T[] _cells;

    /// <summary>
    /// Конструктор.
    /// </summary>
    /// <param name="width">Ширина слоя.</param>
    /// <param name="height">Высота поля.</param>
    /// <param name="fill">Чем заполнять.</param>
    public Layer(int width, int height, T fill = default!)
    {
        // Проверим, что размер валиден.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // Заполним свойства.
        Width = width;
        Height = height;

        // Создаем список.
        _cells = new T[width * height];

        // Заполним поле значениями.
        Array.Fill(_cells, fill);
    }


    /// <summary>
    /// Размер слоя в ширину.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Размер слоя в высоту.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Безопасное окно для чтения списка <see cref="_cells"/>.
    /// </summary>
    public ReadOnlySpan<T> Raw => _cells;

    /// <summary>
    /// Индексатор.
    /// </summary>
    /// <param name="h">Экземпляр гекса.</param>
    public T this[Hex h]
    {
        get => _cells[IndexOf(h)];
        set => _cells[IndexOf(h)] = value;
    }

    /// <summary>
    /// Метод проверки, содержится ли гекс в слое.
    /// </summary>
    /// <param name="h">Экземпляр гекса.</param>
    /// <returns>Вернет флаг, содержится ли гекс в слое или нет.</returns>
    public bool Contains(Hex h)
    {
        (int col, int row) = h.ToOffset();
        return 0 <= col && col < Width && 0 <= row && row < Height;
    }


    /// <summary>
    /// Метод возврата индекса гекса в слое.
    /// </summary>
    /// <param name="h">Экземпляр гекса.</param>
    /// <returns>Индекс гекса в слое.</returns>
    private int IndexOf(Hex h)
    {
        if (!Contains(h))
        {
            throw new ArgumentOutOfRangeException(nameof(h));
        }

        (int col, int row) = h.ToOffset();
        return (row * Width) + col;
    }
}
