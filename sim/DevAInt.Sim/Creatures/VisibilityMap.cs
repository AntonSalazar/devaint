using System;
using DevAInt.Sim.Grid;

namespace DevAInt.Sim.Creatures;

/// <summary>Видимость клеток вокруг глаз: доля каждой клетки от 0 до 1 в квадрате радиуса обзора.</summary>
public sealed class VisibilityMap
{
    /// <summary>Доля видимости, начиная с которой клетка считается видной, например для показа чужих существ.</summary>
    public const float VisibleThreshold = 0.5f;

    /// <summary>Доли видимости по клеткам квадрата построчно.</summary>
    private float[] _coverage = [];

    /// <summary>Клетка глаз, центр квадрата.</summary>
    public CellPos Center { get; private set; }

    /// <summary>Половина стороны квадрата в клетках.</summary>
    public int Radius { get; private set; }

    /// <summary>Сторона квадрата в клетках.</summary>
    public int Size => (Radius * 2) + 1;

    /// <summary>Доля видимости клетки: 0 - не видна, 1 - видна целиком; вне квадрата и на другом этаже - 0.</summary>
    public float Coverage(CellPos cell)
    {
        int dx = cell.X - Center.X;
        int dy = cell.Y - Center.Y;
        if (cell.Z != Center.Z || Math.Abs(dx) > Radius || Math.Abs(dy) > Radius)
        {
            return 0;
        }

        return _coverage[((dy + Radius) * Size) + dx + Radius];
    }

    /// <summary>Новый квадрат вокруг center; массив переиспользуется, если хватает места.</summary>
    internal void Reset(CellPos center, int radius)
    {
        Center = center;
        Radius = radius;
        int cells = Size * Size;
        if (_coverage.Length < cells)
        {
            _coverage = new float[cells];
        }

        Array.Clear(_coverage, 0, cells);
    }

    /// <summary>Доля клетки со смещением dx, dy от центра.</summary>
    internal void Set(int dx, int dy, float coverage) => _coverage[((dy + Radius) * Size) + dx + Radius] = coverage;
}
